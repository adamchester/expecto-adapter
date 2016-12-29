namespace Discovery

open System
open System.Linq
open System.Reflection

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging

open Expecto
open Expecto.Impl

open Filters
open RemotingHelpers
open SourceLocation

type DiscoveryResult =
    struct
        val TestCode:  string
        val TypeName:  string
        val MethodName: string
        new(testCode: string, typeName: string, methodName: string) =
            { TestCode = testCode; TypeName = typeName; MethodName = methodName }
    end

type VsDiscoverCallbackProxy(log: IMessageLogger) =
    inherit MarshalByRefObjectInfiniteLease()
    interface IObserver<string> with
        member this.OnCompleted(): unit = 
            ()
        member x.OnError(error: exn): unit = 
            log.SendMessage(TestMessageLevel.Error, error.ToString())
        member x.OnNext(message: string): unit = 
            log.SendMessage(TestMessageLevel.Informational, message)

type DiscoverProxy(proxyHandler:Tuple<IObserver<string>>) =
    inherit MarshalByRefObjectInfiniteLease()
    let observer = proxyHandler.Item1

    let isFsharpFuncType t =
        let baseType =
            let rec findBase (t:Type) =
                if t.BaseType = typeof<obj> then
                    t
                else
                    findBase t.BaseType
            findBase t
        baseType.IsGenericType && baseType.GetGenericTypeDefinition() = typedefof<FSharpFunc<unit, unit>>

    // If the test function we've found doesn't seem to be in the test assembly, it's
    // possible we're looking at an FsCheck 'testProperty' style check. In that case,
    // the function of interest (i.e., the one in the test assembly, and for which we
    // might be able to find corresponding source code) is referred to in a field
    // of the function object.
    let getFuncTypeToUse (testFunc:TestCode) (asm:Assembly) =
        let t = testFunc.GetType()
        if t.Assembly.FullName = asm.FullName then
            t
        else
            let nestedFunc =
                 t.GetFields()
                |> Seq.tryFind (fun f -> isFsharpFuncType f.FieldType)
            match nestedFunc with
                | Some f -> f.GetValue(testFunc).GetType()
                | None -> t

    member this.DiscoverTests(source: string) =
        let asm = Assembly.LoadFrom(source)
        if not (asm.GetReferencedAssemblies().Any(fun a -> a.Name = "Expecto")) then
            observer.OnNext(sprintf "Skipping: %s because it does not reference Expecto" source)
            Array.empty
        else            
            let tests =
                match testFromAssembly (asm) with
                | Some t -> t
                | None -> TestList ([], Normal)
            Expecto.Test.toTestCodeList tests
            |> Seq.map (fun (name, testFunc, state) ->
                let t = getFuncTypeToUse testFunc asm
                let m =
                    query
                      {
                        for m in t.GetMethods() do
                        where ((m.Name = "Invoke") && (m.DeclaringType = t))
                        exactlyOne
                      }
                new DiscoveryResult(name, t.FullName, m.Name))
            |> Array.ofSeq

[<FileExtension(".dll")>]
[<FileExtension(".exe")>]
[<DefaultExecutorUri(Ids.ExecutorId)>]
type Discoverer() =
    interface ITestDiscoverer with
        member x.DiscoverTests
            (sources: System.Collections.Generic.IEnumerable<string>,
             discoveryContext: IDiscoveryContext,
             logger: IMessageLogger,
             discoverySink: ITestCaseDiscoverySink): unit =
            try
                let vsCallback = new VsDiscoverCallbackProxy(logger)
                for assemblyPath in (sourcesUsingExpecto sources) do
                    use host = new TestAssemblyHost(assemblyPath)
                    let discoverProxy = host.CreateInAppdomain<DiscoverProxy>([|Tuple.Create<IObserver<string>>(vsCallback)|])
                    let testList = discoverProxy.DiscoverTests(assemblyPath)
                    let locationFinder = new SourceLocationFinder(assemblyPath)
                    for { TestCode = code; TypeName = typeName; MethodName = methodName } in testList do
                        let tc = new TestCase(code, Ids.ExecutorUri, assemblyPath)
                        match locationFinder.getSourceLocation typeName methodName with
                        | Some location ->
                            tc.CodeFilePath <- location.SourcePath
                            tc.LineNumber <- location.LineNumber
                        | None -> ()
                        discoverySink.SendTestCase(tc)
            with
            | x -> logger.SendMessage(Logging.TestMessageLevel.Error, x.ToString())

