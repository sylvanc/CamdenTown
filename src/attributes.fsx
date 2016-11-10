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

type QueueAttribute = LiteralAttribute

[<AttributeUsage(AttributeTargets.Method)>]
type QueueTriggerAttribute(queueName: string, connection: string, name: string) =
  inherit TriggerAttribute()
  new (queueName) = QueueTriggerAttribute(queueName, "", "input")
  new (queueName, connection) =
    QueueTriggerAttribute(queueName, connection, "input")

  override __.Check m =
    [ Param m name
        [ typeof<string>
          typeof<byte []>
          typeof<obj>
        ]
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
    [ Result m
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
