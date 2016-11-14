module CamdenTown.Checker

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

open System
open System.Reflection
open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

let private typeList (types: Type list) =
  sprintf
    "%s %s"
    (if types.Length > 1 then "one of" else "a")
    ( types
      |> List.map (fun t -> t.Name)
      |> String.concat ", ")

let private typeInList t (types: Type list) =
  types
  |> List.contains t

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

let NamedType name ty =
  if FSharpType.IsUnion ty then
    let cases = FSharpType.GetUnionCases ty

    if cases.Length = 1 then
      let case = cases.[0]

      if case.Name = name then
        let fields = case.GetFields()

        if fields.Length = 1 then
          let p = fields.[0]
          Some p.PropertyType
        else
          None
      else
        None
    else
      None
  else
    None

let BoundTypes (ty: Type) (types: Type list) =
  let generic = ty.GetGenericTypeDefinition()

  types
  |> List.collect (fun t ->
    [ t; generic.MakeGenericType [| t |] ]
    )

let TypesAsync (types: Type list) =
  BoundTypes typeof<Task<_>> types

let TypesCollector (types: Type list) =
  BoundTypes typeof<ICollector<_>> types

let TypesAsyncCollector (types: Type list) =
  BoundTypes typeof<ICollector<_>> types

let Param (m: MethodInfo) name (types: Type list) =
  _param m name types false

let OptParam (m: MethodInfo) name (types: Type list) =
  _param m name types true

let Result (m: MethodInfo) (types: Type list) =
  let taskTypes = TypesAsync types
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

let DNSName (name: string) =
  let alphanumeric (c: char) =
    (c >= 'A' && c <= 'Z') ||
    (c >= 'a' && c <= 'z') ||
    (c >= '0' && c <= '9')
  let validdns (c: char) = (c = '-') || alphanumeric c

  if
    name.Length >= 3 &&
    name.Length <= 63 &&
    alphanumeric (name.Chars(0)) &&
    alphanumeric (name.Chars(name.Length - 1)) &&
    (name.ToCharArray() |> Array.forall validdns)
  then
    None, []
  else
    None, [sprintf "'%s' is not a valid DNS name" name]
