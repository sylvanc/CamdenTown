#load "src/functionapp.fsx"

open CamdenTown.Manage
open CamdenTown.FunctionApp

// Create an Azure service principal and fill it in
// https://azure.microsoft.com/en-us/documentation/articles/resource-group-authenticate-service-principal-cli/

let creds =
  { SubscriptionID = "Your subscription UUID"
    TenantID = "Your tenand UUID"
    ClientID = "The service principal name"
    ClientSecret = "The service principal password"
  }

let rg =
  { Name = "Your resource group name"
    Location = "Your resource group location"
  }

let sa =
  { Name = "Your storage account name"
    Sku = "Your storage account replication service level"
  }

let plan =
  { Name = "Your app service name"
    SkuName = "B1"
    Capacity = 1u
  }

let name = "Your Function App name"

let App () =
  AzureFunctionApp(creds, rg, sa, plan, name)
