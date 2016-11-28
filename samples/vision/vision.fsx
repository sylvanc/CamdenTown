#load "../../build/CamdenTown.fsx"
#load "../../creds-private.fsx"

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.IO
open Newtonsoft.Json
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure
open Microsoft.WindowsAzure.Storage.Table
open CamdenTown.FunctionApp
open CamdenTown.Attributes

let callVisionAPI (image: Stream) =
  async {
    let url = "https://api.projectoxford.ai/vision/v1.0/analyze?visualFeatures=Description"

    use client = new HttpClient()
    client.DefaultRequestHeaders.Add(
      "Ocp-Apim-Subscription-Key",
      Creds.ComputerVision
      )

    use content = new StreamContent(image)
    content.Headers.ContentType <-
      MediaTypeHeaderValue("application/octet-stream")

    let! httpResponse = client.PostAsync(url, content) |> Async.AwaitTask

    if httpResponse.StatusCode = HttpStatusCode.OK then
      return! httpResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
    else
      return null
  }

type Images = Blob of Stream
type Descriptions = Queue of string

[<BlobTrigger(typeof<Images>, "{name}")>]
[<QueueOutput(typeof<Descriptions>)>]
let ImageDescription (input: Stream, name: string, log: TraceWriter) =
  let desc = callVisionAPI(input) |> Async.RunSynchronously
  let result = sprintf "{\"%s\": %s}" name desc
  log.Info(result)
  result

let Test (app: AzureFunctionApp) (files: string list) =
  let inC = app.Container<Images, Stream>()
  let outQ = app.Queue<Descriptions, string>()

  files
  |> List.iter (fun file ->
    inC.UploadFile(Path.GetFileName(file), file)
  )

  files
  |> List.map (fun _ -> outQ.WaitAndPop())

let TestLocal (files: string list) =
  let log = TraceLocal(Diagnostics.TraceLevel.Verbose)

  files
  |> List.map (fun file ->
    use stream = new FileStream(file, FileMode.Open)
    let name = Path.GetFileName(file)
    ImageDescription(stream, name, log)
  )
