module Creds

(*
  Create an Azure service principal and fill it in below.

  1. Install the Azure Cross-Platform CLI
    https://github.com/Azure/azure-xplat-cli/
  2. Log in to Azure:
    > azure login
  3. Display your account info, recording the SubscriptionID (displayed
    as ID) and the TenantID (displayed as Tenant ID):
    > azure account show
  4. Create a client:
    > azure ad sp create -n {ClientID} -p {ClientSecret}
  5. This will display an application object ID GUID. Permission that
    application object ID as a Contributor for your subscription:
    > azure role assignment create --objectId {application object ID} -o Contributor -c /subscriptions/{SubscriptionID}/
*)

let SubscriptionID = "Your subscription UUID"
let TenantID = "Your tenand UUID"
let ClientID = "The service principal name"
let ClientSecret = "The service principal password"

// Sign up for computer vision api
// https://www.microsoft.com/cognitive-services/en-us/computer-vision-api 
let ComputerVision = "A computer vision API key"
