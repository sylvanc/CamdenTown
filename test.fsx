#load "samples/samples.fsx"

open System
open System.IO
open System.Diagnostics
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

open CamdenTown.Attributes
open CamdenTown.FunctionApp

// Here we set up our Azure Function App
// Create a creds-private.fsx based on creds.fsx
#load "creds-private.fsx"

let app =
  AzureFunctionApp(
    name = "DynaBadger999",
    group = "DynaBadgers",
    storage = "dynabadgerstorage",
    planName = "DynaBadgerFarm",

    subId = Creds.SubscriptionID,
    tenantId = Creds.TenantID,
    clientId = Creds.ClientID,
    clientSecret = Creds.ClientSecret,

    replication = "Standard_LRS",
    plan = "Y1",
    location = "westeurope",
    capacity = 0
  )

// Display the log in the REPL
let log = app.Log (printfn "%s")

// Deploy Vision.ImageDescription to Azure Functions
app.Deploy [Vision.ImageDescription]

let files =
  [ "samples/vision/bull-landscape-nature-mammal-144234.jpeg"
    "samples/vision/pexels-photo-107889.jpeg"
    "samples/vision/pexels-photo-238000.jpeg"
  ]

let localDesc = Vision.TestLocal files
let remoteDesc = Vision.Test app files

// Shutdown the log.
log.Cancel()
