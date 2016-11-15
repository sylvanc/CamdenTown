module CamdenTown.Attributes

#load "checker.fsx"

#r "System.Net.Http"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.IO
open System.Reflection
open System.Net.Http
open System.Text.RegularExpressions
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq
open CamdenTown.Checker

// F# attributes don't inherit AttributeUsage

[<AbstractClass>]
type AzureAttribute() =
  inherit Attribute()
  abstract member Check: MethodInfo -> (string option * string list) list
  default __.Check mi = []
  abstract member Json: unit -> JObject list
  default __.Json () = []
  abstract member Build: string -> string list
  default __.Build(dir) = []

[<AbstractClass>]
type TriggerAttribute() =
  inherit AzureAttribute()

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

  override __.Check mi = [Param mi name [typeof<string>]]

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

  override __.Check mi =
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
          Param mi name [qType.Value]
        else
          None, []
      )
      DNSName ty.Name
      OptParam mi "expirationTime" [typeof<DateTimeOffset>]
      OptParam mi "insertionTime" [typeof<DateTimeOffset>]
      OptParam mi "nextVisisbleTime" [typeof<DateTimeOffset>]
      OptParam mi "queueTrigger" [typeof<string>]
      OptParam mi "id" [typeof<string>]
      OptParam mi "popReceipt" [typeof<string>]
      OptParam mi "dequeueCount" [typeof<int>]
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
type QueueOutputAttribute(ty: Type, name: string) =
  inherit AzureAttribute()
  new (ty) = QueueOutputAttribute(ty, "$return")

  override __.Check mi =
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
            None, ["The queue output type must be a string, a byte [], or an object type"]
        else
          None, []
      )
      DNSName ty.Name
      ( if qType.IsSome then
          let t = [qType.Value]

          if name = "$return" then
            Result mi (
              [ t
                TypesCollector t
                TypesAsyncCollector t
              ] |> List.concat)
          else
            Param mi name (
              [ TypesCollector t
                TypesAsyncCollector t
              ] |> List.concat)
        else
          None, []
      )
    ]

  override __.Json () =
    [ JObject(
        [ JProperty("type", "queue")
          JProperty("queueName", ty.Name)
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", name)
          JProperty("direction", "out")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type TimerTriggerAttribute(schedule: string, name: string) =
  inherit TriggerAttribute()
  new (schedule) = TimerTriggerAttribute(schedule, "timer")
  override __.Check mi = [Param mi name [typeof<TimerInfo>]]

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
  inherit TriggerAttribute()
  new () = HttpTriggerAttribute("req")

  override __.Check mi =
    [ Param mi name [typeof<HttpRequestMessage>]
      Result mi [typeof<HttpResponseMessage>]
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

[<AttributeUsage(AttributeTargets.Method)>]
type BlobTriggerAttribute(ty: Type, path: string, name: string) =
  inherit TriggerAttribute()

  let re =
    Regex(
      @"(?:^|[^{]){(@?[_\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}][\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}\p{Cf}]*)}(?:[^}]|$)"
      )

  new (ty, path) = BlobTriggerAttribute(ty, "input")

  override __.Check mi =
    let qType = NamedType "Blob" ty

    List.append
      [ ( if qType.IsNone then
            None, ["The blob must be some Blob of 'T"]
          else
            if
              ( [ typeof<Stream>
                  typeof<TextReader>
                  typeof<string>
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
              None, ["The blob trigger input type must be a Stream, TextReader, string, obj, or CLIMutable record type"]
        )
        ( if qType.IsSome then
            Param mi name [qType.Value]
          else
            None, []
        )
        DNSName ty.Name
      ]
      ( // Bind path variables to method parameters.
        re.Matches path
        |> Seq.cast
        |> Seq.map (fun (m: Match) ->
          Param mi (m.Groups.[1].Value) [typeof<string>]
          )
        |> List.ofSeq
      )

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "blobTrigger")
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", name)
          JProperty("path", path)
          JProperty("direction", "in")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type BlobInputAttribute(ty: Type, path: string, name: string) =
  inherit TriggerAttribute()

  let re =
    Regex(
      @"(?:^|[^{]){(@?[_\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}][\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}\p{Cf}]*)}(?:[^}]|$)"
      )

  new (ty, path) = BlobInputAttribute(ty, "input")

  override __.Check mi =
    let qType = NamedType "Blob" ty

    List.append
      [ ( if qType.IsNone then
            None, ["The blob must be some Blob of 'T"]
          else
            if
              ( [ typeof<Stream>
                  typeof<TextReader>
                  typeof<string>
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
              None, ["The blob input type must be a Stream, TextReader, string, obj, or CLIMutable record type"]
        )
        ( if qType.IsSome then
            Param mi name [qType.Value]
          else
            None, []
        )
        DNSName ty.Name
      ]
      ( // Check that path variables exist as method parameters.
        re.Matches path
        |> Seq.cast
        |> Seq.map (fun (m: Match) ->
          ExistsParam mi (m.Groups.[1].Value) [typeof<string>]
          )
        |> List.ofSeq
      )

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "blob")
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", name)
          JProperty("path", path)
          JProperty("direction", "in")
        ])
    ]

[<AttributeUsage(AttributeTargets.Method)>]
type BlobOutputAttribute(ty: Type, path: string, name: string) =
  inherit TriggerAttribute()

  let re =
    Regex(
      @"(?:^|[^{]){(@?[_\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}][\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}\p{Cf}]*)}(?:[^}]|$)"
      )

  new (ty, path) = BlobOutputAttribute(ty, "$return")

  override __.Check mi =
    let qType = NamedType "Blob" ty

    List.append
      [ ( if qType.IsNone then
            None, ["The blob must be some Blob of 'T"]
          else
            if
              ( [ typeof<Stream>
                  typeof<TextReader>
                  typeof<string>
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
              None, ["The blob output type must be a Stream, TextReader, string, obj, or CLIMutable record type"]
        )
        ( if qType.IsSome then
            let t = [qType.Value]

            if name = "$return" then
              Result mi (
                [ t
                  TypesCollector t
                  TypesAsyncCollector t
                ] |> List.concat)
            else
              Param mi name (
                [ TypesCollector t
                  TypesAsyncCollector t
                ] |> List.concat)
          else
            None, []
        )
        DNSName ty.Name
      ]
      ( // Check that path variables exist as method parameters.
        re.Matches path
        |> Seq.cast
        |> Seq.map (fun (m: Match) ->
          ExistsParam mi (m.Groups.[1].Value) [typeof<string>]
          )
        |> List.ofSeq
      )

  override __.Json () =
    [ new JObject(
        [ JProperty("type", "blob")
          JProperty("connection", "AzureWebJobsStorage")
          JProperty("name", name)
          JProperty("path", path)
          JProperty("direction", "out")
        ])
    ]
