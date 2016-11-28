#load "../../build/CamdenTown.fsx"

open System.Text
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open CamdenTown.Attributes

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
        Encoding.UTF8,
        "application/json"
      )
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- content
    return resp
  } |> Async.StartAsTask



let A = [|1.0; 2.0; 3.0|]
let B = [|1.0; 2.0; 3.0|]
let prod X Y = Array.map2(fun a b -> a * b) X Y |> Array.sum

[<HttpTrigger>]
let TestDeps1(req: HttpRequestMessage) =
  async {        
    let! body =
      if not (isNull req.Content) then
        req.Content.ReadAsStringAsync() |> Async.AwaitTask
      else
        async { return "" }
    let respcode = HttpStatusCode.OK
    let contentstr = sprintf "%A" (prod A B)
    let resp = new HttpResponseMessage(respcode)
    let content = new StringContent(contentstr, Encoding.UTF8, "text/html")
    resp.Content <- content
    return resp
  } |> Async.StartAsTask
