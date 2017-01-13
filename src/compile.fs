namespace CamdenTown

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

module Compile =

  module private Helpers =
    let replace (pattern: string) (replacement: string) (source: string) =
      source.Replace(pattern, replacement)

    let createAzureFiles
      localDir (mi: MethodInfo) (attrs: AzureAttribute list) =
      let dllTemplate =
        """
#I @"[[ASSEMBLYDIRECTORY]]"
#r "System.Net.Http"
#r "Newtonsoft.Json.dll"
#r "Microsoft.Azure.WebJobs.dll"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"

[[ASSEMBLIES]]

open System.IO
open System.Reflection
open MBrace.Vagabond
open MBrace.Vagabond.ExportableAssembly

let mutable unpickle: ([[FUNCTYPE]]) option = None

let [[FUNCNAME]]Execute([[PARAMETERS]]) =
  if unpickle.IsNone then
    let dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
    let vagabond = Vagabond.Initialize()
    let serializer = vagabond.Serializer

    let asmPickle = File.ReadAllBytes(dir + "/pickle.asm")
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

    let pickle = File.ReadAllBytes(dir + "/pickle.bin")
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
          let ty = TypeName param.ParameterType
          ty, sprintf "%s: %s" param.Name ty, param.Name)
        |> Array.unzip3

      let functype =
        sprintf
          "%s -> %s"
          (argtypes |> String.concat " * ")
          (TypeName mi.ReturnType)

      let assemblies =
        Directory.GetFiles(localDir, "*.dll")
        |> Array.map (Path.GetFileName >> sprintf "#r \"./%s\"")
        |> String.concat "\n"

      let asmdir =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

      let dllSource =
        dllTemplate
        |> replace "[[ASSEMBLYDIRECTORY]]" asmdir
        |> replace "[[ASSEMBLIES]]" assemblies
        |> replace "[[FUNCNAME]]" mi.Name
        |> replace "[[FUNCTYPE]]" functype
        |> replace "[[PARAMETERS]]" (ps |> String.concat ", ")
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

      if code <> 0 then
        let errtext = errors |> Array.map (fun err -> err.ToString())
        failwith <| String.concat "\n" errtext

      File.Delete(dllFile)

      let template =
          """
[[ASSEMBLIES]]
#r "./[[DLLNAME]]"

let Run([[PARAMETERS]]) =
  [[FUNCNAME]].[[FUNCNAME]]Execute([[ARGUMENTS]])
"""

      let source =
        template
        |> replace "[[ASSEMBLIES]]" assemblies
        |> replace "[[DLLNAME]]" (Path.GetFileName(dllName))
        |> replace "[[FUNCNAME]]" mi.Name
        |> replace "[[PARAMETERS]]" (ps |> String.concat ", ")
        |> replace "[[ARGUMENTS]]" (args |> String.concat ", ")

      let file = sprintf "%s/run.fsx" localDir
      File.WriteAllText(file, source)

      let bindings =
        attrs
        |> List.collect (fun attr -> attr.Json())

      let json =
        JObject(
          [ JProperty("disabled", false)
            JProperty("bindings", bindings)
          ])

      File.WriteAllText(sprintf "%s/function.json" localDir, json.ToString())

    let checkTrigger (mi: MethodInfo) (attrs: AzureAttribute list) =
      let (psList, errorsList) =
        attrs
        |> List.collect (fun attr -> attr.Check mi)
        |> List.unzip

      let ps = (Log mi)::psList |> List.choose id
      (Multibound ps)::
      (Unbound mi ps)::
      (UnboundResult mi ps)::
      errorsList
      |> List.concat

    let compileTrigger dir x (mi: MethodInfo) (attrs: AzureAttribute list) =
      let localDir = sprintf "%s/%s" dir mi.Name
      let pickleFile = sprintf "%s/pickle.bin" localDir
      let asmFile = sprintf "%s/pickle.asm" localDir
      Directory.CreateDirectory(localDir) |> ignore

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

      let asmdir =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

      [ "FsPickler.dll"
        "Vagabond.dll"
        "Mono.Cecil.dll"
      ]
      |> List.iter (fun file ->
        let source = sprintf "%s/%s" asmdir file
        let target = sprintf "%s/%s" localDir (Path.GetFileName(file))
        File.Copy(source, target)
      )

      createAzureFiles localDir mi attrs
      localDir, attrs |> List.collect (fun attr -> attr.Build localDir)

    let getAttrs (mi: MethodInfo) =
      let azureAttrs =
        mi.GetCustomAttributes(true)
        |> Array.choose (function
          | :? AzureAttribute as attr -> Some attr
          | _ -> None
          )

      let triggerAttrs =
        azureAttrs
        |> Array.choose (fun attr ->
          match attr with
          | :? TriggerAttribute -> Some attr
          | _ -> None
          )

      let errors =
        match triggerAttrs.Length with
        | 0 -> ["Function has no trigger attribute"]
        | 1 -> []
        | _ -> ["Function has more than one trigger attribute"]

      List.ofArray azureAttrs, errors

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
      |> List.map (fun (_, mi) ->
        let (attr, errors) = getAttrs mi
        let errors =
          if errors.IsEmpty then
            checkTrigger mi attr
          else
            errors

        mi.DeclaringType.FullName, mi.Name, errors
      )

    static member CheckMIs (mis: (obj * MethodInfo) list) =
      mis
      |> List.map (fun (_, mi) ->
        let (attr, errors) = getAttrs mi
        mi.DeclaringType.FullName, mi.Name, attr, errors
      )

    static member CompileExpr
      ( dir: string,
        exprs: Quotations.Expr<obj list>
        ) =
      let guid = Guid.NewGuid().ToString()
      let buildDir = sprintf "%s/%s" dir guid

      Compiler.GetMI exprs
      |> List.map (fun (x, mi) ->
        let (attr, errors) = getAttrs mi
        let errors =
          if errors.IsEmpty then
            checkTrigger mi attr
          else
            errors
        let (filename, errors) =
          if errors.IsEmpty then
            compileTrigger buildDir x mi attr
          else
            null, errors

        mi.DeclaringType.FullName, mi.Name, filename, errors
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
