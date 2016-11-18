// Create a credentials file based on creds.fsx and #load it here
#load "creds-private.fsx"

open System
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

open CamdenTown.Attributes
open CamdenTown.FunctionApp

// Here we set up our Azure Function App
let app = Creds.App()

[<CLIMutable>]
type Foo = {
  Name: string
  Food: string
}

type Queue1 = Queue of Foo
type Queue2 = Queue of Foo
type Queue3 = Queue of Foo

[<QueueTrigger(typeof<Queue1>)>]
[<QueueOutput(typeof<Queue2>)>]
[<QueueOutput(typeof<Queue3>, "q3")>]
let QueueHandler(input: Foo, q3: ICollector<Foo>, log: TraceWriter) =
  async {
    log.Error(sprintf "%s likes %s" input.Name input.Food)
    q3.Add({ Name = "Mrs. " + input.Name; Food = input.Food + " and crackers" })
    return { Name = "Mr. " + input.Name; Food = input.Food + " and cheese" }
  } |> Async.StartAsTask

[<QueueTrigger(typeof<Queue2>)>]
let QueueSecond(input: Foo, log: TraceWriter) =
  log.Error(sprintf "%s wants %s" input.Name input.Food)

[<QueueTrigger(typeof<Queue3>)>]
let QueueThird(input: Foo, log: TraceWriter) =
  log.Error(sprintf "%s wants %s" input.Name input.Food)

app.Deploy [ QueueHandler; QueueSecond; QueueThird ]

let logLevel = app.LogLevel()
let log = app.Log()
log.Cancel()

let q1 = app.Queue<Queue1, Foo>()
let r1 = q1.Push({ Name = "Smith"; Food = "snacks" })

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
  "numbers": [%s]
}
"""
          req.Method.Method
          (req.RequestUri.ToString())
          body
          (numbers |> List.map string |> String.concat ", "),
        Text.Encoding.UTF8,
        "application/json"
      )
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- content
    return resp
  } |> Async.StartAsTask

[<TimerTrigger("*/10 * * * * *")>]
let TimerToLog(timer: TimerInfo, log: TraceWriter) =
  log.Error(sprintf "Executed at %s" (DateTimeOffset.Now.ToString()))

// TODO:
// TableIn, TableOut
// handle auth token expiration
// create service principal
//  https://azure.microsoft.com/en-gb/documentation/articles/resource-group-authenticate-service-principal-cli/
//  ~/.azure has access tokens and subscription info after a CLI login
// create needed queues
//  statically define them to avoid string referencing?
// bind a storage account
// set log level on webapp
// stream log

app.Deploy [ TimerToLog ]
app.Deploy [ HttpClosure ]
app.Undeploy [ TimerToLog ]
app.Undeploy [ HttpClosure ]
app.Restart()
app.Delete()

// TODO: FaceLocator in CamdenTown
// check into a samples directory
// _maybe_ draw the rectangles on the images
// image comparison?

// allow multiple attributes on some things
// fire triggers from REPL

// TEST:
// queue out not via $return
// blob trigger
// blob in
// blob out via $return
// blob out not via $return
