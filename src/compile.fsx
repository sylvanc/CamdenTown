module CamdenTown.Compile

#load "attributes.fsx"

#r "../packages/System.Reflection.Metadata/lib/portable-net45+win8/System.Reflection.Metadata.dll"
#r "../packages/FSharp.Compiler.Service/lib/net45/FSharp.Compiler.Service.dll"
#r "../packages/FsPickler/lib/net45/FsPickler.dll"
#r "../packages/Vagabond/lib/net45/Vagabond.dll"
#r "../packages/Mono.Cecil/lib/net45/Mono.Cecil.dll"

open System
open System.IO
open System.Reflection
open FSharp.Quotations
open FSharp.Quotations.Patterns
open Newtonsoft.Json.Linq
open MBrace.Vagabond
open MBrace.Vagabond.AssemblyProtocols
open MBrace.Vagabond.ExportableAssembly
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.SimpleSourceCodeServices
open CamdenTown.Attributes
open CamdenTown.Checker

module private Helpers =
  let rec typeName (t: Type) =
    if t = typeof<System.Void> then
      "unit"
    elif t.IsArray then
      sprintf "%s []" (typeName (t.GetElementType()))
    elif t.IsGenericType then
      let name = t.FullName
      let tick = name.IndexOf '`'
      sprintf "%s<%s>"
        ( if tick > 0 then name.Remove tick else name )
        ( t.GetGenericArguments()
          |> Array.map typeName
          |> String.concat ", "
        )
    else
      t.FullName

  let replace (pattern: string) (replacement: string) (source: string) =
    source.Replace(pattern, replacement)

  let runFile localDir (mi: MethodInfo) =
    let dllTemplate =
      """
#r "System.Net.Http"
#r "../../../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../../../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../../../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../../../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

#r "FsPickler.dll"
#r "Mono.Cecil.dll"
#r "Vagabond.dll"

open System.IO
open System.Reflection
open MBrace.Vagabond
open MBrace.Vagabond.ExportableAssembly

let mutable unpickle: ([[FUNCTYPE]]) option = None

let [[FUNCNAME]]Execute([[PARAMETERS]]) =
  if unpickle.IsNone then
    let vagabond = Vagabond.Initialize()
    let serializer = vagabond.Serializer

    let asmPickle = File.ReadAllBytes(__dir + "/pickle.asm")
    let exAsm = serializer.UnPickle<ExportableAssembly []>(asmPickle)

    let vas =
      [|
        for ex in exAsm do
          let results =
            try
              vagabond.CacheRawAssemblies([|ex|])
            with e ->
              [||]
          yield! results
      |]

    let lr = vagabond.LoadVagabondAssemblies(vas)

    let pickle = File.ReadAllBytes(__dir + "/pickle.bin")
    unpickle <- Some (serializer.UnPickle<obj>(pickle) :?> [[FUNCTYPE]])

  try
    unpickle.Value([[ARGUMENTS]])
  with e ->
    let rec show (ex: exn) =
      if isNull ex then
        ""
      else
        sprintf
          "ERROR: %s\n%s\n%s"
          ex.Message
          ex.StackTrace
          (show ex.InnerException)
    failwith (show e)
"""

    let (argtypes, ps, args) =
      mi.GetParameters()
      |> Array.map (fun param ->
        let ty = typeName param.ParameterType
        ty, sprintf "%s: %s" param.Name ty, param.Name)
      |> Array.unzip3

    let functype =
      sprintf
        "%s -> %s"
        (argtypes |> String.concat " * ")
        (typeName mi.ReturnType)

    let dllSource =
      dllTemplate
      |> replace "[[FUNCNAME]]" mi.Name
      |> replace "[[FUNCTYPE]]" functype
      |> replace "[[PARAMETERS]]"
        (Array.append ps [| "__dir: string" |] |> String.concat ", ")
      |> replace "[[ARGUMENTS]]" (args |> String.concat ", ")

    let dllFile = sprintf "%s/%s.fsx" localDir mi.Name 
    File.WriteAllText(dllFile, dllSource)

    let checker = FSharpChecker.Create()
    let options =
      checker.GetProjectOptionsFromScript(dllFile, dllSource)
      |> Async.RunSynchronously

    let dllName = Path.ChangeExtension(dllFile, "dll")
    let flags =
      Array.concat
        [|
          [|
            "fsc.exe"
            "--simpleresolution"
            "--out:" + dllName
          |]
          options.OtherOptions
          options.ProjectFileNames
        |]

    let compiler = SimpleSourceCodeServices()
    let (errors, code) = compiler.Compile(flags)

    let assembly =
      if code = 0 then
        Assembly.LoadFrom(dllName)
      else
        let errtext = errors |> Array.map (fun err -> err.ToString())
        failwith <| String.concat "\n" errtext

    File.Delete(dllFile)

    let template =
        """
#r "[[DLLNAME]]"

let Run([[PARAMETERS]]) =
  [[FUNCNAME]].[[FUNCNAME]]Execute([[ARGUMENTS]])
"""

    let source =
      template
      |> replace "[[DLLNAME]]" (Path.GetFileName(dllName))
      |> replace "[[FUNCNAME]]" mi.Name
      |> replace "[[PARAMETERS]]" (ps |> String.concat ", ")
      |> replace "[[ARGUMENTS]]"
        (Array.append args [| "__SOURCE_DIRECTORY__" |] |> String.concat ", ")

    let file = sprintf "%s/run.fsx" localDir
    File.WriteAllText(file, source)

  let functionJson localDir (attrs: AzureAttribute list) =
    let bindings =
      attrs
      |> List.collect (fun attr -> attr.Json())

    let json =
      JObject(
        [ JProperty("disabled", false)
          JProperty("bindings", bindings)
        ])

    File.WriteAllText(sprintf "%s/function.json" localDir, json.ToString())

  let checkTrigger x (mi: MethodInfo) (attrs: AzureAttribute list) =
    let (psList, errorsList) =
      attrs
      |> List.collect (fun attr -> attr.Check mi)
      |> List.unzip

    let ps = (Log mi)::psList |> List.choose id
    (Unbound mi ps)::errorsList |> List.concat

  let compileTrigger dir x (mi: MethodInfo) (attrs: AzureAttribute list) =
    let localDir = sprintf "%s/%s" dir mi.Name
    let pickleFile = sprintf "%s/pickle.bin" localDir
    let asmFile = sprintf "%s/pickle.asm" localDir
    let di = Directory.CreateDirectory(localDir)

    let isIgnoredAssemblies (asm: Assembly) =
      [ "FSharp.Compiler.Interactive.Settings"
        "FsPickler"
        "Newtonsoft.Json"
        "System.Linq"
        "System.Net.Http"
        "System.Reflection"
        "System.Resources.ResourceManager"
        "System.Runtime"
        "System.Runtime.Extensions"
        "System.Security"
        "System.Configuration"
        "System.Spatial"
        "System.Threading"
        "System.Threading.Tasks"
        "System.Threading.Tasks.Dataflow"
        "Microsoft.Data.Edm"
        "Microsoft.Data.OData"
        "Microsoft.Data.Services.Client"
        "Microsoft.WindowsAzure.Storage"
        "Microsoft.Azure.WebJobs"
        "Microsoft.Azure.WebJobs.Host"
        "Microsoft.Azure.WebJobs.Extensions"
      ] |> List.contains (asm.GetName().Name)

    let manager = Vagabond.Initialize(cacheDirectory = localDir, isIgnoredAssembly = isIgnoredAssemblies)
    let deps = manager.ComputeObjectDependencies(x, permitCompilation = true)

    let rdeps = manager.CreateRawAssemblies(deps)
    let pickleRdeps = manager.Serializer.Pickle rdeps
    File.WriteAllBytes(asmFile, pickleRdeps)

    let pickle = manager.Serializer.Pickle x
    File.WriteAllBytes(pickleFile, pickle)

    [ "../packages/FsPickler/lib/net45/FsPickler.dll"
      "../packages/Vagabond/lib/net45/Vagabond.dll"
      "../packages/Mono.Cecil/lib/net45/Mono.Cecil.dll"
    ]
    |> List.iter (fun file ->
      let source = sprintf "%s/%s" __SOURCE_DIRECTORY__ file
      let target = sprintf "%s/%s" localDir (Path.GetFileName(file))
      File.Copy(source, target)
    )

    functionJson localDir attrs
    runFile localDir mi

    localDir, attrs |> List.collect (fun attr -> attr.Build localDir)

  let getResultAttrs (attrs: AzureAttribute []) =
    let resultAttrs =
      attrs
      |> Array.choose (fun attr ->
        match attr with
        | :? ResultAttribute -> Some(attr)
        | :? ComplexAttribute -> Some(attr)
        | _ -> None
        )

    if resultAttrs.Length = 0 then
      [| NoResultAttribute() :> AzureAttribute |]
    else
      resultAttrs

  let getAttrs (mi: MethodInfo) =
    let azureAttrs =
      mi.GetCustomAttributes(false)
      |> Array.choose (function
        | :? AzureAttribute as attr -> Some attr
        | _ -> None
        )

    let triggerAttrs =
      azureAttrs
      |> Array.choose (fun attr ->
        match attr with
        | :? TriggerAttribute -> Some(attr)
        | :? ComplexAttribute -> Some(attr)
        | _ -> None
        )

    let resultAttrs = getResultAttrs azureAttrs

    let triggerErrors =
      match triggerAttrs.Length with
      | 0 -> ["Function has no Trigger attribute"]
      | 1 -> []
      | _ -> ["Function has more than one Trigger attribute"]

    let errors =
      if resultAttrs.Length > 1 then
        "Function has more than one Result attribute"::triggerErrors
      else
        triggerErrors

    mi, List.ofArray azureAttrs, errors

  let rec (|TheCall|_|) x =
    match x with
    | Call(_, mi, _) -> Some(mi)
    | Let(_, _, TheCall(mi)) -> Some(mi)
    | _ -> None

  let (|TheLambda|_|) x =
    match x with
    | Lambda(_, TheCall(mi)) -> Some(mi)
    | Coerce(Lambda(_, TheCall(mi)), o) when o.Name = "Object" -> Some(mi)
    | _ -> None

  let (|Empty|_|) = function
    | NewUnionCase (uc, _) when uc.Name = "Empty" -> Some ()
    | _ -> None

  let (|Cons|_|) = function
    | NewUnionCase (uc, [hd; tl]) when uc.Name = "Cons" -> Some(hd, tl)
    | _ -> None

  let rec (|List|_|) (|P|_|) = function
    | Cons(P(hd), List (|P|_|) (tl)) -> Some(hd::tl)
    | Empty -> Some([])
    | _ -> None

open Helpers

type Compiler =
  static member GetMI (xs: Quotations.Expr<obj list>) =
    match xs with
    | WithValue(x, _, TheLambda(mi)) -> [x, mi]
    | WithValue(x, _, List (|TheLambda|_|) mis) -> List.zip (x :?> obj list) mis
    | _ -> failwith "Not a function"

  static member CheckExpr
    ( exprs: Quotations.Expr<obj list>
      ) =
    Compiler.GetMI exprs
    |> List.map (
      (fun (x, mi) -> x, getAttrs mi) >>
      (fun (x, (mi, attr, errors)) ->
        let errors =
          if errors.IsEmpty then
            checkTrigger x mi attr
          else
            errors

        mi.DeclaringType.FullName, mi.Name, errors
      )
    )

  static member CompileExpr
    ( dir: string,
      exprs: Quotations.Expr<obj list>
      ) =
    let buildDir = sprintf "%s/%s" dir (Guid.NewGuid().ToString())

    Compiler.GetMI exprs
    |> List.map (
      (fun (x, mi) -> x, getAttrs mi) >>
      (fun (x, (mi, attr, errors)) ->
        if errors.IsEmpty then
          x, mi, attr, checkTrigger x mi attr
        else
        x, mi, attr, errors) >>
      (fun (x, mi, attr, errors) ->
        let (filename, errors) =
          if errors.IsEmpty then
            compileTrigger buildDir x mi attr
          else
            null, errors

        mi.DeclaringType.FullName, mi.Name, filename, errors
      )
    )

  static member Check
    ( [<ReflectedDefinition(true)>] exprs: Quotations.Expr<obj list>
      ) =
    Compiler.CheckExpr(exprs)

  static member Compile
    ( dir: string,
      [<ReflectedDefinition(true)>] exprs: Quotations.Expr<obj list>
      ) =
    Compiler.CompileExpr(dir, exprs)

  static member Dump
    ( [<ReflectedDefinition(true)>] exprs: Quotations.Expr<obj list>
      ) =
    exprs
