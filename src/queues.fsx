module CamdenTown.Queues

#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/WindowsAzure.Storage/lib/net40/Microsoft.WindowsAzure.Storage.dll"

open System
open System.Reflection
open Newtonsoft.Json
open FSharp.Reflection
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue
open Microsoft.WindowsAzure.Storage.Blob

type LiveQueue<'T>(q: CloudQueue) =
  let isString = typeof<'T> = typeof<string>
  let isByteArray = typeof<'T> = typeof<byte []>

  member __.Push (input: 'T) =
    if isString then
      q.AddMessage(CloudQueueMessage(input :> obj :?> string))
    else if isByteArray then
      q.AddMessage(CloudQueueMessage(input :> obj :?> byte []))
    else
      let data = JsonConvert.SerializeObject(input)
      q.AddMessage(CloudQueueMessage(data))

  member __.Pop () =
    let m = q.GetMessage()
    if not (isNull m) then
      let r =
        if isString then
          Some (m.AsString :> obj :?> 'T)
        else if isByteArray then
          Some (m.AsBytes :> obj :?> 'T)
        else
          let r = JsonConvert.DeserializeObject<'T>(m.AsString)
          Some r

      q.DeleteMessage(m)
      r
    else
      None

type LiveContainer<'T>(c: CloudBlobContainer) =
  let x = c.GetBlockBlobReference("foo")
  member __.Create (name: string, input: 'T) =
    ()
