module Creds

(*
  Create an Azure service principal and fill it in below.

  1. Install the Azure Cross-Platform CLI
    https://github.com/Azure/azure-xplat-cli/
  2. Log in to Azure:
    > azure login
    as ID) and the TenantID (displayed as Tenant ID):
    > azure account show
  4. Create a client with a made up a ClientID and ClientSecret e.g. dynabadger123
    and record below the Service Principal Name displayed (probably has an http:// prefix):
    > azure ad sp create -n {ClientID} -p {ClientSecret}
  5. Having created a client with an ObjectID (displayed as Object Id), permission that
    ObjectID as a Contributor for your subscription:
    > azure role assignment create --objectId {ObjectID} -o Contributor -c /subscriptions/{SubscriptionID}/
*)

let SubscriptionID = "Your subscription UUID"
let TenantID = "Your tenand UUID"
let ClientID = "The service principal name"
let ClientSecret = "The service principal password"

// Sign up for computer vision api
// https://www.microsoft.com/cognitive-services/en-us/computer-vision-api 
let ComputerVision = "A computer vision API key"
