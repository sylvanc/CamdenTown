module CamdenTown.FunctionApp

#load "rest.fsx"
#load "checker.fsx"
#load "compile.fsx"
#load "storage.fsx"
#load "queues.fsx"
#load "manage.fsx"

open System
open System.Threading
open Microsoft.WindowsAzure.Storage
open CamdenTown.Rest
open CamdenTown.Queues
open CamdenTown.Manage
open CamdenTown.Checker
open CamdenTown.Compile

type AzureFunctionApp
  (creds: Credentials, rg: ResourceGroup, sa: StorageAccount,
    plan: AppServicePlan, name: string, ?dir: string) =

  let attempt resp =
    resp
    |> Async.RunSynchronously
    |> CheckResponse

  let result resp =
    resp
    |> Async.RunSynchronously
    |> AsyncChoice

  let buildDir = defaultArg dir "build"
  let auth = GetAuth creds |> result

  do
    CreateResourceGroup auth rg |> attempt
    CreateStorageAccount auth rg sa |> attempt
    CreateAppServicePlan auth rg plan |> attempt
    CreateFunctionApp auth rg plan name |> attempt

  let kuduAuth = KuduAuth auth rg name |> result
  let storageKeys = StorageAccountKeys auth rg sa |> result
  let connectionString =
    sprintf "DefaultEndpointsProtocol=https;AccountName=%s;AccountKey=%s"
      sa.Name
      storageKeys.Head
  let storageAccount = CloudStorageAccount.Parse(connectionString)
  let queueClient = storageAccount.CreateCloudQueueClient()

  do
    attempt (
      SetAppSettings auth rg name
        [ "AzureWebJobsDashboard", connectionString
          "AzureWebJobsStorage", connectionString
          "FUNCTIONS_EXTENSION_VERSION", "~1"
          "AZUREJOBS_EXTENSION_VERSION", "beta"
          "WEBSITE_NODE_DEFAULT_VERSION", "4.1.2" ]
      )

  member __.Auth () = auth
  member __.KuduAuth () = kuduAuth
  member __.StorageKey () = storageKeys.Head

  member __.Queue<'T, 'U> () =
    let q =
      let ty = typeof<'T>
      let qt = NamedType "Queue" ty
      if qt.IsSome then
        let ty2 = typeof<'U>
        if qt.Value = ty2 then
          let name = ty.Name.ToLowerInvariant()
          let q = queueClient.GetQueueReference(name)
          q.CreateIfNotExists() |> ignore
          q
        else
          failwithf "%s is not a Queue of %s" ty.Name ty2.Name
      else
        failwith "Not a queue"

    LiveQueue<'U>(q)

  member __.Deploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name |> Async.RunSynchronously with
    | OK _ ->
      let r = Compiler.CompileExpr(buildDir, xs)
      let ok = r |> List.forall (fun (_, _, _, errors) -> errors.IsEmpty)
      let resp =
        r
        |> List.map (fun (typ, func, path, errors) ->
          if errors.IsEmpty then
            if ok then
              DeleteFunction auth rg name func
              |> Async.RunSynchronously
              |> ignore

              let target = sprintf "site/wwwroot/%s" func
              match
                KuduVfsPutDir kuduAuth target path
                |> Async.RunSynchronously
                with
              | OK _ -> OK func
              | Error(reason, text) ->
                Error((sprintf "%s: %s" func reason), text)
            else
              OK func
          else
            Error(func, String.concat "\n" errors)
        )
      (StartFunctionApp auth rg name |> Async.RunSynchronously)::resp
    | x -> [x]

  member __.Undeploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name |> Async.RunSynchronously with
    | OK _ ->
      let resp =
        Compiler.GetMI xs
        |> List.map (
          (fun (x, mi) -> mi.Name) >>
          (fun func ->
            DeleteFunction auth rg name func
            |> Async.RunSynchronously
            )
          )
      (StartFunctionApp auth rg name |> Async.RunSynchronously)::resp
    | x -> [x]

  member __.Start () =
    StartFunctionApp auth rg name |> Async.RunSynchronously

  member __.Stop () =
    StopFunctionApp auth rg name |> Async.RunSynchronously

  member __.Restart () =
    RestartFunctionApp auth rg name |> Async.RunSynchronously

  member __.Delete () =
    DeleteFunctionApp auth rg name |> Async.RunSynchronously

  member __.Log () =
    let cancel = new CancellationTokenSource()
    let loop =
      async {
        use! c = Async.OnCancel(fun () -> printfn "Log stopped")
        let! token = Async.CancellationToken
        let! stream = KuduAppLog kuduAuth

        while not token.IsCancellationRequested do
          let! line = stream.ReadLineAsync() |> Async.AwaitTask
          if not token.IsCancellationRequested then
            printfn "%s" line
      }
    Async.Start(loop, cancel.Token)
    cancel

  member __.LogLevel () =
    KuduLogLevel kuduAuth |> Async.RunSynchronously
