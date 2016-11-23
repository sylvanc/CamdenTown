module CamdenTown.Queues

#I "../packages/Newtonsoft.Json/lib/net45"
#r "Newtonsoft.Json.dll"
#I "../packages/WindowsAzure.Storage/lib/net40"
#r "Microsoft.WindowsAzure.Storage.dll"

open System
open System.IO
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

  member this.WaitAndPop () =
    let rec fetch () =
      match this.Pop() with
      | Some x -> x
      | None ->
        Async.Sleep(500) |> Async.RunSynchronously
        fetch ()
    fetch ()

type LiveContainer<'T>(c: CloudBlobContainer) =
  let isStream = typeof<'T> = typeof<Stream>
  let isTextReader = typeof<'T> = typeof<TextReader>
  let isString = typeof<'T> = typeof<string>

  member __.UploadFile (name, path) =
    let blob = c.GetBlockBlobReference(name)
    blob.UploadFromFile(path)

  member __.UploadBytes (name, bytes) =
    let blob = c.GetBlockBlobReference(name)
    blob.UploadFromByteArray(bytes, 0, bytes.Length)

  member __.UploadString (name, text) =
    let blob = c.GetBlockBlobReference(name)
    blob.UploadText(text)

  member __.Upload (name, value: 'T) =
    let blob = c.GetBlockBlobReference(name)

    let text =
      if isStream then
        failwith "Can't upload a stream"
      elif isTextReader then
        (value :> obj :?> TextReader).ReadToEnd()
      elif isString then
        value :> obj :?> string
      else
        JsonConvert.SerializeObject(value)

    blob.UploadText(text)
