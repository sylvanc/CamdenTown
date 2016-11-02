#load "testfunctions.fsx"

open System
open CamdenTown.Manage
open CamdenTown.Functions
open Testfunctions

// Create an Azure service principal and fill it in
// https://azure.microsoft.com/en-us/documentation/articles/resource-group-authenticate-service-principal-cli/

let creds =
  { SubscriptionID = "Your subscription UUID"
    TenantID = "Your tenand UUID"
    ClientID = "The service principal name"
    ClientSecret = "The service principal password"
  }

let rg =
  { Name = "Badgers"
    Location = "ukwest"
  }

let plan =
  { Name = "BadgerFarm"
    SkuName = "B1"
    Capacity = 1u
  }

let name = "badger" + Guid.NewGuid.ToString()

let functions = AzureFunctionApp(creds, rg, plan, name)
functions.deploy [ HttpClosure ]
functions.delete()
