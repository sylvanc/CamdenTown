#load "src/functions.fsx"

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

open CamdenTown.Functions

let numbers = [ for i in 1..10 -> i * i ]

[<HttpTrigger>]
let HttpClosure(req: HttpRequestMessage, log: TraceWriter) =
  async {
    log.Error(sprintf "HttpClosure: %s" (req.RequestUri.ToString()))
    let! body =
      if not (isNull req.Content) then
        req.Content.ReadAsStringAsync() |> Async.AwaitTask
      else
        async { return "" }

    let content =
      new StringContent(
        sprintf
          """
{
  "method": "%s",
  "uri": "%s",
  "body": "%s",
  "numbers": "%s"
}
"""
          req.Method.Method
          (req.RequestUri.ToString())
          body
          (numbers |> List.map (fun i -> string i) |> String.concat ","),
        Text.Encoding.UTF8,
        "application/json"
      )
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- content
    return resp
  } |> Async.StartAsTask

[<ManualTrigger>]
let ByHand(input: string, log: TraceWriter) =
  log.Info(sprintf "Triggered: %s" input)

// [<Queue>]
// let inQ = "in-queue"

// [<Queue>]
// let outQ = "out-queue"

// // TODO: could go back and find inQ and look for attributes on it
// [<QueueTrigger(inQ)>]
// [<QueueResult(outQ)>]
// let QueueHandler(input: String) =
//   async {
//     return "Processed: " + input
//   } |> Async.StartAsTask

// // TODO: webapp shuts down when a timer is deployed
// [<TimerTrigger("*/10 * * * * *")>]
// let TimerToLog(timer: TimerInfo, log: TraceWriter) =
//   log.Error(sprintf "Executed at %s" (DateTimeOffset.Now.ToString()))

// TableIn, TableOut, BlobTrigger, BlobIn, BlobOut


// TODO:
// handle auth token expiration
// create service principal
//  https://azure.microsoft.com/en-gb/documentation/articles/resource-group-authenticate-service-principal-cli/
//  ~/.azure has access tokens and subscription info after a CLI login
// create needed queues
//  statically define them to avoid string referencing?
// bind a storage account
// set log level on webapp
// stream log
