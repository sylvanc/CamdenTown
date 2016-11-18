module CamdenTown.Rest

#r "System.Net.Http"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.Net
open System.Net.Http
open Newtonsoft.Json

type Response =
  | OK of string
  | Error of string * string

let makeClient (token: string) =
  let client = new HttpClient()
  if not (isNull token) then
    client.DefaultRequestHeaders.Add("Authorization", token)
  client

let makeJson text =
  let content = new StringContent(text)
  content.Headers.ContentType.MediaType <- "application/json"
  content

let get token (uri: string) = async {
  use client = makeClient token
  return! client.GetAsync(uri) |> Async.AwaitTask
}

let getStream token (uri: string) = async {
  use client = makeClient token
  return!
    client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
    |> Async.AwaitTask
}

let post token data (uri: string) = async {
  use client = makeClient token
  let content = makeJson data
  return! client.PostAsync(uri, content) |> Async.AwaitTask
}

let put token data (uri: string) = async {
  use client = makeClient token
  let content = makeJson data
  return! client.PutAsync(uri, content) |> Async.AwaitTask
}

let delete token (uri: string) = async {
  use client = makeClient token
  return! client.DeleteAsync(uri) |> Async.AwaitTask
}

let parseResponse (response: Async<HttpResponseMessage>) =
  async {
    let! res = response
    let! content =
      res.Content.ReadAsStringAsync()
      |> Async.AwaitTask

    if res.IsSuccessStatusCode then
      return OK content
    else
      return Error(res.ReasonPhrase, content)
  }
