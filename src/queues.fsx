module CamdenTown.Queues

#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/WindowsAzure.Storage/lib/net40/Microsoft.WindowsAzure.Storage.dll"

open System
open System.Reflection
open Newtonsoft.Json
open FSharp.Reflection
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue

type LiveQueue<'T>(q: CloudQueue) =
  member __.Push (input: 'T) =
    let data = JsonConvert.SerializeObject(input)
    q.AddMessage(CloudQueueMessage(data))

  member __.Pop () =
    let m = q.GetMessage()

    if not (isNull m) then
      let r = JsonConvert.DeserializeObject<'T>(m.AsString)
      q.DeleteMessage(m)
      Some r
    else
      None
