module CamdenTown.Checker

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

open System
open System.Reflection
open System.Threading.Tasks
open Microsoft.Azure.WebJobs.Host

let private typesAsync (types: Type list) =
  let task = typeof<Task<_>>
  let generic = task.GetGenericTypeDefinition()

  types
  |> List.collect (fun t ->
    [ t; generic.MakeGenericType [| t |] ]
    )

let private typeList (types: Type list) =
  sprintf
    "%s %s"
    (if types.Length > 1 then "one of" else "a")
    ( types
      |> List.map (fun t -> t.Name)
      |> String.concat ", ")

let private typeInList t (types: Type list) =
  types
  |> List.exists (fun typ -> t = typ || t.IsSubclassOf typ)

let private _param (m: MethodInfo) name (types: Type list) optional =
  let nameErr () =
    sprintf "Must have a parameter named '%s'" name
  let typeErr () =
    sprintf
      "Parameter '%s' must be %s"
      name
      (typeList types)

  match
    m.GetParameters()
    |> Array.tryFind (fun p -> p.Name = name)
    with
  | Some p when not (types |> typeInList p.ParameterType) ->
    Some name, [typeErr()]
  | Some p ->
    Some name, []
  | None when not optional ->
    Some name, [nameErr(); typeErr()]
  | None ->
    None, []

let Param (m: MethodInfo) name (types: Type list) =
  _param m name types false

let OptParam (m: MethodInfo) name (types: Type list) =
  _param m name types true

let Result (m: MethodInfo) (types: Type list) =
  let taskTypes = typesAsync types
  if not (taskTypes |> typeInList m.ReturnType) then
    None, [sprintf "Return type must be %s" (typeList taskTypes)]
  else
    None, []

let Log (m: MethodInfo) =
  match
    m.GetParameters()
    |> Array.tryFind (fun p ->
      [typeof<TraceWriter>] |> typeInList p.ParameterType)
    with
  | Some p -> Some p.Name
  | None -> None

let Unbound (m: MethodInfo) ps =
  m.GetParameters()
  |> Array.filter (fun p -> not (List.contains p.Name ps))
  |> Array.map (fun p -> sprintf "Parameter '%s' is not bound" p.Name)
  |> List.ofArray
