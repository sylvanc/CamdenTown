module CamdenTown.Manage

#r "System.IO.Compression"
#r "System.IO.Compression.FileSystem"
#r "System.Net.Http"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Text
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type Response =
  | OK of string
  | Error of string * string

module private Helpers =
  let makeClient (token: string) =
    let client = new HttpClient()
    if not (isNull token) then
      client.DefaultRequestHeaders.Add("Authorization", token)
    client

  let makeJson text =
    let content = new StringContent(text)
    content.Headers.ContentType.MediaType <- "application/json"
    content

  let get token (uri: string) =
    async {
      use client = makeClient token
      return!
        client.GetAsync(uri)
        |> Async.AwaitTask
    } |> Async.RunSynchronously

  let post token data (uri: string) =
    async {
      use client = makeClient token
      let content = makeJson data
      return!
        client.PostAsync(uri, content)
        |> Async.AwaitTask
    } |> Async.RunSynchronously

  let put token data (uri: string) =
    async {
      use client = makeClient token
      let content = makeJson data
      return!
        client.PutAsync(uri, content)
        |> Async.AwaitTask
    } |> Async.RunSynchronously

  let delete token (uri: string) =
    async {
      use client = makeClient token
      return!
        client.DeleteAsync(uri)
        |> Async.AwaitTask
    } |> Async.RunSynchronously

  let ResourceGroupUri subscriptionId name =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s?api-version=2015-11-01"
      subscriptionId name

  let StorageAccountUri subscriptionId rgName name =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Storage/storageAccounts/%s?api-version=2016-01-01"
      subscriptionId rgName name

  let AppServicePlanUri subscriptionId rgName name =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/serverfarms/%s?api-version=2015-08-01"
      subscriptionId rgName name

  let AppServiceUri subscriptionId rgName name =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s?api-version=2015-08-01"
      subscriptionId rgName name

  let VfsUri name path =
    sprintf
      "https://%s.scm.azurewebsites.net/api/vfs/%s/"
      name
      path

  let parseResponse (response: HttpResponseMessage) =
    let resp =
      async {
        return!
          response.Content.ReadAsStringAsync()
          |> Async.AwaitTask
      } |> Async.RunSynchronously

    if response.IsSuccessStatusCode then
      OK resp
    else
      Error(response.ReasonPhrase, resp)

open Helpers

type Credentials = {
  SubscriptionID: string
  TenantID: string
  ClientID: string
  ClientSecret: string
}

type ResourceGroup = {
  Name: string
  Location: string
}

type StorageAccount = {
  Name: string
  Sku: string
}

type AppServicePlan = {
  Name: string
  SkuName: string
  Capacity: uint32
}

type Auth = {
  Token: string
  SubscriptionID: string
}

let GetAuth (creds: Credentials) =
  let r =
    async {
      use client = new HttpClient()
      let uri =
        sprintf
          "https://login.windows.net/%s/oauth2/token"
          creds.TenantID
      let text =
        sprintf
          "resource=%s&client_id=%s&grant_type=client_credentials&client_secret=%s"
          (WebUtility.UrlEncode("https://management.core.windows.net/"))
          (WebUtility.UrlEncode(creds.ClientID))
          (WebUtility.UrlEncode(creds.ClientSecret))

      let content = new StringContent(text, Encoding.UTF8, "application/x-www-form-urlencoded")

      return! client.PostAsync(uri, content) |> Async.AwaitTask
    }
    |> Async.RunSynchronously
    |> parseResponse

  match r with
  | OK text ->
    let json = JObject.Parse(text)
    let token = json.["access_token"].Value<string>()
    { Token = sprintf "Bearer %s" token
      SubscriptionID = creds.SubscriptionID
    }
  | Error(reason, text) ->
    failwithf "%s: %s" reason text

let CreateResourceGroup auth (rg: ResourceGroup) =
  ResourceGroupUri auth.SubscriptionID rg.Name
  |> put auth.Token (
    sprintf """
{
  "location": "%s"
}
"""
      rg.Location)
  |> parseResponse

let DeleteResourceGroup auth (rg: ResourceGroup) =
  ResourceGroupUri auth.SubscriptionID rg.Name
  |> delete auth.Token
  |> parseResponse

let CreateStorageAccount auth (rg: ResourceGroup) (sa: StorageAccount) =
  StorageAccountUri auth.SubscriptionID rg.Name sa.Name
  |> put auth.Token (
    sprintf """
{
  "location": "%s",
  "properties": {
    "encryption": {
      "services": {
        "blob": {
          "enabled": true
        }
      },
      "keySource": "Microsoft.Storage"
    }
  },
  "sku": {
    "name": "%s"
  },
  "kind": "Storage"
}
"""
      rg.Location
      sa.Sku
    )
  |> parseResponse

let DeleteStorageAccount auth (rg: ResourceGroup) (sa: StorageAccount) =
  StorageAccountUri auth.SubscriptionID rg.Name sa.Name
  |> delete auth.Token
  |> parseResponse

let StorageAccountKeys auth (rg: ResourceGroup) (sa: StorageAccount) =
  let r =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Storage/storageAccounts/%s/listKeys?api-version=2016-01-01"
      auth.SubscriptionID rg.Name sa.Name
    |> post auth.Token ""
    |> parseResponse

  match r with
  | OK text ->
    let json = JObject.Parse(text)
    let keys = json.["keys"].Value<JArray>()

    keys.Children()
    |> Seq.filter (fun key ->
      let prop = key.Value<JObject>()
      let perm = prop.["permissions"].Value<string>()
      String.Compare(perm, "full", StringComparison.OrdinalIgnoreCase) = 0
      )
    |> Seq.map (fun key ->
      let prop = key.Value<JObject>()
      prop.["value"].Value<string>()
      )
    |> List.ofSeq
  | Error(reason, text) ->
    failwithf "%s: %s" reason text

let CreateAppServicePlan auth (rg: ResourceGroup) (plan: AppServicePlan) =
  AppServicePlanUri auth.SubscriptionID rg.Name plan.Name
  |> put auth.Token (
    sprintf """
{
  "location": "%s",
  "Sku": {
    "Name": "%s",
    "Capacity": %d
  }
}
"""
      rg.Location
      plan.SkuName
      plan.Capacity
    )
  |> parseResponse

let DeleteAppServicePlan auth (rg: ResourceGroup) (plan: AppServicePlan) =
  AppServicePlanUri auth.SubscriptionID rg.Name plan.Name
  |> delete auth.Token
  |> parseResponse

let SetAppSettings
  auth (rg: ResourceGroup) name (settings: (string * string) list) =
  sprintf 
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/config/appsettings?api-version=2015-08-01"
    auth.SubscriptionID
    rg.Name
    name
  |> put auth.Token (
    let props =
      settings
      |> List.map (fun (key, value) ->
        sprintf
          "%s: %s"
          (JsonConvert.ToString(key))
          (JsonConvert.ToString(value))
        )
      |> String.concat ",\n    "

    sprintf """
{
  "properties": {
    %s
  }
}
"""
      props
    )
  |> parseResponse

let CreateFunctionApp auth (rg: ResourceGroup) (plan: AppServicePlan) name =
  AppServiceUri auth.SubscriptionID rg.Name name
  |> put auth.Token (
    sprintf """
{
  "kind": "functionapp",
  "location": "%s",
  "properties": { "serverFarmId": "%s" }
}
"""
      rg.Location
      plan.Name)
  |> parseResponse

let StartFunctionApp auth (rg: ResourceGroup) name =
  sprintf
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/start?api-version=2015-08-01"
    auth.SubscriptionID rg.Name name
  |> post auth.Token ""
  |> parseResponse

let StopFunctionApp auth (rg: ResourceGroup) name =
  sprintf
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/stop?api-version=2015-08-01"
    auth.SubscriptionID rg.Name name
  |> post auth.Token ""
  |> parseResponse

let RestartFunctionApp auth (rg: ResourceGroup) name =
  sprintf
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/restart?api-version=2015-08-01"
    auth.SubscriptionID rg.Name name
  |> post auth.Token ""
  |> parseResponse

let DeleteFunctionApp auth (rg: ResourceGroup) name =
  AppServiceUri auth.SubscriptionID rg.Name name
  |> delete auth.Token
  |> parseResponse

let ListFunctions auth (rg: ResourceGroup) name =
  sprintf
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/functions?api-version=2015-08-01"
    auth.SubscriptionID rg.Name name
  |> get auth.Token
  |> parseResponse

let DeleteFunction auth (rg: ResourceGroup) name funcName =
  sprintf
    "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/functions/%s?api-version=2015-08-01"
    auth.SubscriptionID rg.Name name funcName
  |> delete auth.Token
  |> parseResponse

type KuduAuth = {
  Token: string
  Name: string
}

let KuduAuth auth (rg: ResourceGroup) name =
  let r =
    sprintf
      "https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Web/sites/%s/config/publishingcredentials/list?api-version=2015-08-01"
      auth.SubscriptionID
      rg.Name
      name
    |> post auth.Token ""
    |> parseResponse

  match r with
  | OK text ->
    let json = JObject.Parse(text)
    let user = json.["properties"].["publishingUserName"].Value<string>()
    let pass = json.["properties"].["publishingPassword"].Value<string>()
    let bytes = Encoding.ASCII.GetBytes(sprintf "%s:%s" user pass)
    { Token = "Basic " + Convert.ToBase64String(bytes)
      Name = name
    }
  | Error(reason, text) ->
    failwithf "%s: %s" reason text

let KuduVersion auth =
  sprintf
    "https://%s.scm.azurewebsites.net/api/environment"
    auth.Name
  |> get auth.Token
  |> parseResponse

let KuduVfsGet auth path =
  sprintf
    "https://%s.scm.azurewebsites.net/api/vfs/%s"
    auth.Name
    path
  |> get auth.Token
  |> parseResponse

let KuduVfsLs auth path =
  VfsUri auth.Name path
  |> get auth.Token
  |> parseResponse

let KuduVfsMkdir auth path =
  VfsUri auth.Name path
  |> put auth.Token ""
  |> parseResponse

let KuduVfsPutDir auth target source =
  let zip = source + ".zip"
  if File.Exists(zip) then
    File.Delete(zip)
  ZipFile.CreateFromDirectory(source, zip)
  KuduVfsMkdir auth target |> ignore

  let uri =
    sprintf
      "https://%s.scm.azurewebsites.net/api/zip/%s"
      auth.Name
      target

  async {
    use client = makeClient auth.Token
    let data = File.ReadAllBytes(zip)
    File.Delete(zip)
    let content = new ByteArrayContent(data)
    return!
      client.PutAsync(uri, content)
      |> Async.AwaitTask
  }
  |> Async.RunSynchronously
  |> parseResponse
