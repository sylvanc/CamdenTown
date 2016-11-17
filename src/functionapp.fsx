module CamdenTown.FunctionApp

#load "manage.fsx"
#load "compile.fsx"

open System.Threading
open CamdenTown.Manage
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

  let auth = GetAuth creds |> result

  do
    CreateResourceGroup auth rg |> attempt
    CreateStorageAccount auth rg sa |> attempt
    CreateAppServicePlan auth rg plan |> attempt
    CreateFunctionApp auth rg plan name |> attempt

    let keys = StorageAccountKeys auth rg sa |> result

    let connectionString =
      sprintf "DefaultEndpointsProtocol=https;AccountName=%s;AccountKey=%s"
        sa.Name
        keys.Head

    attempt (
      SetAppSettings auth rg name
        [ "AzureWebJobsDashboard", connectionString
          "AzureWebJobsStorage", connectionString
          "FUNCTIONS_EXTENSION_VERSION", "~1"
          "AZUREJOBS_EXTENSION_VERSION", "beta"
          "WEBSITE_NODE_DEFAULT_VERSION", "4.1.2" ]
      )

  let kuduAuth = KuduAuth auth rg name |> result
  let buildDir = defaultArg dir "build"

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
