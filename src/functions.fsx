module CamdenTown.Functions

#load "manage.fsx"
#load "compile.fsx"

open System
open System.IO
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq
open CamdenTown.Manage
open CamdenTown.Compile

// F# attributes don't inherit AttributeUsage

[<AttributeUsage(AttributeTargets.Method)>]
type ReferencesAttribute(refs: string [], path: string) =
  inherit AzureAttribute()
  override __.Build(dir) =
    refs
    |> Array.map (fun ref -> sprintf "%s/%s" path ref)
    |> Array.iter (fun ref ->
        let file = Path.GetFileName(ref)
        File.Copy(ref, sprintf "%s/%s" dir file)
      )
    []

[<AttributeUsage(AttributeTargets.Method)>]
type ManualTriggerAttribute(name: string) =
  inherit TriggerAttribute()
  new () = ManualTriggerAttribute("input")

  override __.Check m = [Check.Param m name [typeof<string>]]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "manualTrigger")
          JProperty("name", name)
          JProperty("direction", "in")
        ])
    ]

type QueueAttribute = LiteralAttribute

[<AttributeUsage(AttributeTargets.Method)>]
type QueueTriggerAttribute(queueName: string, connection: string, name: string) =
  inherit TriggerAttribute()
  new (queueName) = QueueTriggerAttribute(queueName, "", "input")
  new (queueName, connection) =
    QueueTriggerAttribute(queueName, connection, "input")

  override __.Check m =
    [ Check.Param m name
        [ typeof<string>
          typeof<byte []>
          typeof<obj>
        ]
      Check.OptParam m "expirationTime" [typeof<DateTimeOffset>]
      Check.OptParam m "insertionTime" [typeof<DateTimeOffset>]
      Check.OptParam m "nextVisisbleTime" [typeof<DateTimeOffset>]
      Check.OptParam m "queueTrigger" [typeof<string>]
      Check.OptParam m "id" [typeof<string>]
      Check.OptParam m "popReceipt" [typeof<string>]
      Check.OptParam m "dequeueCount" [typeof<int>]
    ]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "queueTrigger")
          JProperty("queueName", queueName)
          JProperty("connection", connection)
          JProperty("name", name)
          JProperty("direction", "in")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type QueueResultAttribute(queueName: string, connection: string) =
  inherit ResultAttribute()
  new (queueName) = QueueResultAttribute(queueName, "")

  override __.Check m =
    [ Check.Result m
        [ typeof<string>
          typeof<byte []>
          typeof<obj>
          typeof<ICollector<_>>
          typeof<IAsyncCollector<_>>
        ]
    ]

  override __.Json () =
    [ JObject(
        [ JProperty("type", "queue")
          JProperty("queueName", queueName)
          JProperty("connection", connection)
          JProperty("name", "$return")
          JProperty("direction", "out")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type TimerTriggerAttribute(schedule: string, name: string) =
  inherit TriggerAttribute()
  new (schedule) = TimerTriggerAttribute(schedule, "timer")
  override __.Check m = [Check.Param m name [typeof<TimerInfo>]]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "timerTrigger")
          JProperty("name", name)
          JProperty("schedule", schedule)
          JProperty("direction", "in")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type HttpTriggerAttribute(name: string) =
  inherit ComplexAttribute()
  new () = HttpTriggerAttribute("req")

  override __.Check m =
    [ Check.Param m name [typeof<HttpRequestMessage>]
      Check.Result m [typeof<HttpResponseMessage>]
    ]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "httpTrigger")
          JProperty("name", name)
          JProperty("webHookType", "")
          JProperty("direction", "in")
          JProperty("authLevel", "anonymous")
        ])
      new JObject(
        [ JProperty("type", "http")
          JProperty("name", "unused")
          JProperty("direction", "out")
        ])
    ]

type AzureFunctionApp
  (creds: Credentials, rg: ResourceGroup, plan: AppServicePlan, name: string,
    ?dir: string) =

  let auth = GetAuth creds

  let attempt resp =
    match resp with
    | OK _ -> ()
    | Error(reason, text) -> failwithf "%s: %s" reason text

  do
    attempt (CreateResourceGroup auth rg)
    attempt (CreateAppServicePlan auth rg plan)
    attempt (CreateFunctionApp auth rg plan name)
    attempt (SetAppsetting auth rg name "FUNCTIONS_EXTENSION_VERSION" "latest")

  let kuduAuth = KuduAuth auth rg name
  let buildDir = defaultArg dir "build"

  member __.deploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name with
    | OK _ ->
      let r = Compiler.compileExpr(buildDir, xs)
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

  member __.undeploy
    ( [<ReflectedDefinition(true)>] xs: Quotations.Expr<obj list>
      ) =
    // Restart the function app so that loaded DLLs don't prevent deletion
    match StopFunctionApp auth rg name with
    | OK _ ->
      let resp =
        Compiler.getMI xs
        |> List.map (fun (x, mi) -> mi.Name)
        |> List.map (fun func -> DeleteFunction auth rg name func)
      (StartFunctionApp auth rg name)::resp
    | x -> [x]

  member __.delete () =
    DeleteFunctionApp auth rg name
