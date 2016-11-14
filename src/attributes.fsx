module CamdenTown.Attributes

#load "checker.fsx"

#r "System.Net.Http"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.IO
open System.Reflection
open System.Net.Http
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq
open CamdenTown.Checker

// F# attributes don't inherit AttributeUsage

[<AbstractClass>]
type AzureAttribute() =
  inherit Attribute()
  abstract member Check: MethodInfo -> (string option * string list) list
  default __.Check(mi) = []
  abstract member Json: unit -> JObject list
  default __.Json() = []
  abstract member Build: string -> string list
  default __.Build(dir) = []

[<AbstractClass>]
type TriggerAttribute() =
  inherit AzureAttribute()

[<AbstractClass>]
type ResultAttribute() =
  inherit AzureAttribute()

[<AbstractClass>]
type ComplexAttribute() =
  inherit AzureAttribute()

[<AttributeUsage(AttributeTargets.Method)>]
type NoResultAttribute() =
  inherit ResultAttribute()
  override __.Check m = [Result m [typeof<unit>]]

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

  override __.Check m = [Param m name [typeof<string>]]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "manualTrigger")
          JProperty("name", name)
          JProperty("direction", "in")
        ])
    ]

type Queue<'T>() =
  static member private Bind = typeof<'T>

[<AttributeUsage(AttributeTargets.Method)>]
type QueueTriggerAttribute(ty: Type, name: string) =
  inherit TriggerAttribute()
  new (ty) = QueueTriggerAttribute(ty, "input")

  override __.Check m =
    let qType = NamedType "Queue" ty

    [ ( if qType.IsNone then
          None, ["The queue must be some Queue of 'T"]
        else
          if
            ( [ typeof<string>
                typeof<byte []>
                typeof<obj>
              ]
              |> List.contains qType.Value)
            ||
            ( qType.Value.GetCustomAttributes()
              |> Seq.exists (function
                | :? CLIMutableAttribute -> true
                | _ -> false))
          then
            None, []
          else
            None, ["The queue trigger input type must be a string, byte[], obj, or CLIMutable record type"]
      )
      ( if qType.IsSome then
          Param m name [qType.Value]
        else
          None, []
      )
      DNSName ty.Name
      OptParam m "expirationTime" [typeof<DateTimeOffset>]
      OptParam m "insertionTime" [typeof<DateTimeOffset>]
      OptParam m "nextVisisbleTime" [typeof<DateTimeOffset>]
      OptParam m "queueTrigger" [typeof<string>]
      OptParam m "id" [typeof<string>]
      OptParam m "popReceipt" [typeof<string>]
      OptParam m "dequeueCount" [typeof<int>]
    ]

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "queueTrigger")
          JProperty("queueName", ty.Name)
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", name)
          JProperty("direction", "in")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type QueueResultAttribute(ty: Type) =
  inherit ResultAttribute()

  override __.Check m =
    let qType = NamedType "Queue" ty

    [ ( if qType.IsNone then
          None, ["The queue must be some Queue of 'T"]
        else
          None, []
      )
      ( if qType.IsSome then
          if
            ( [ typeof<string>
                typeof<byte []>
                typeof<obj>
              ]
              |> List.contains qType.Value)
            ||
            (qType.Value.IsSubclassOf typeof<obj>)
          then
            None, []
          else
            None, ["The queue result output type must be a string, a byte [], or an object type"]
        else
          None, []
      )
      DNSName ty.Name
      ( if qType.IsSome then
          Result m (
            let t = [qType.Value]
            [ t
              TypesCollector t
              TypesAsyncCollector t
            ] |> List.concat
            )
        else
          None, []
      )
    ]

  override __.Json () =
    [ JObject(
        [ JProperty("type", "queue")
          JProperty("queueName", ty.Name)
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", "$return")
          JProperty("direction", "out")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type TimerTriggerAttribute(schedule: string, name: string) =
  inherit TriggerAttribute()
  new (schedule) = TimerTriggerAttribute(schedule, "timer")
  override __.Check m = [Param m name [typeof<TimerInfo>]]

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
    [ Param m name [typeof<HttpRequestMessage>]
      Result m [typeof<HttpResponseMessage>]
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
