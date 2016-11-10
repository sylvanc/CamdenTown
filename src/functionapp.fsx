module CamdenTown.FunctionApp

#load "manage.fsx"
#load "compile.fsx"

open CamdenTown.Manage
open CamdenTown.Compile

type AzureFunctionApp
  (creds: Credentials, rg: ResourceGroup, sa: StorageAccount,
    plan: AppServicePlan, name: string, ?dir: string) =

  let auth = GetAuth creds

  let attempt resp =
    match resp with
    | OK _ -> ()
    | Error(reason, text) -> failwithf "%s: %s" reason text

  do
    attempt (CreateResourceGroup auth rg)
    attempt (CreateStorageAccount auth rg sa)
    attempt (CreateAppServicePlan auth rg plan)
    attempt (CreateFunctionApp auth rg plan name)

    let keys = StorageAccountKeys auth rg sa
    let connectionString =
      sprintf "DefaultEndpointsProtocol=https;AccountName=%s;AccountKey=%s"
        sa.Name
        keys.Head

    attempt (
      SetAppSettings auth rg name
        [ "AzureWebJobsDashboard", connectionString
          "AzureWebJobsStorage", connectionString
          "FUNCTIONS_EXTENSION_VERSION", "~0.9"
          "AZUREJOBS_EXTENSION_VERSION", "beta"
          "WEBSITE_NODE_DEFAULT_VERSION", "4.1.2" ]
      )

  let kuduAuth = KuduAuth auth rg name
  let buildDir = defaultArg dir "build"

  member __.Deploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name with
    | OK _ ->
      let r = Compiler.CompileExpr(buildDir, xs)
      let ok = r |> List.forall (fun (_, _, _, errors) -> errors.IsEmpty)
      let resp =
        r
        |> List.map (fun (typ, func, path, errors) ->
          if errors.IsEmpty then
            if ok then
              DeleteFunction auth rg name func |> ignore
              let target = sprintf "site/wwwroot/%s" func
              match KuduVfsPutDir kuduAuth target path with
              | OK _ -> OK func
              | Error(reason, text) ->
                Error((sprintf "%s: %s" func reason), text)
            else
              OK func
          else
            Error(func, String.concat "\n" errors)
        )
      (StartFunctionApp auth rg name)::resp
    | x -> [x]

  member __.Undeploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name with
    | OK _ ->
      let resp =
        Compiler.GetMI xs
        |> List.map (
          (fun (x, mi) -> mi.Name) >>
          (fun func -> DeleteFunction auth rg name func)
        )
      (StartFunctionApp auth rg name)::resp
    | x -> [x]

  member __.Start () =
    StartFunctionApp auth rg name

  member __.Stop () =
    StopFunctionApp auth rg name

  member __.Restart () =
    RestartFunctionApp auth rg name

  member __.Delete () =
    DeleteFunctionApp auth rg name
