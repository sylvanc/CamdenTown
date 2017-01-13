#load "../../build/CamdenTown.fsx"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open CamdenTown.FunctionApp
open CamdenTown.Attributes

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
    log.Warning(sprintf "%s likes %s" input.Name input.Food)
    q3.Add({ Name = "Mrs. " + input.Name; Food = input.Food + " and crackers" })
    return { Name = "Mr. " + input.Name; Food = input.Food + " and cheese" }
  } |> Async.StartAsTask

[<QueueTrigger(typeof<Queue2>)>]
let QueueSecond(input: Foo, log: TraceWriter) =
  log.Info(sprintf "%s wants %s" input.Name input.Food)

[<QueueTrigger(typeof<Queue3>)>]
let QueueThird(input: Foo, log: TraceWriter) =
  log.Verbose(sprintf "%s wants %s" input.Name input.Food)

let trigger (app: AzureFunctionApp) name food =
  let inQ = app.Queue<Queue1, Foo>()
  inQ.Push({Name = name; Food = food})
