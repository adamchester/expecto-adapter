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

    let getFuncTypeAndMethodToUse (testFunc:TestCode) (asm:Assembly) =
        let traverseObjectGraph (root : obj) =
            //Safeguard against circular references
            let rec inner traversed current = seq {
                let currentComparable = {
                    new obj() with
                        member __.Equals(other) = current.Equals other
                        member __.GetHashCode() = current.GetHashCode()
                    interface IComparable with
                        member this.CompareTo(other) =
                              this.GetHashCode().CompareTo(other.GetHashCode())
                }
                if current = null ||
                   box current :? Pointer ||
                   Set.contains currentComparable traversed then do () else

                let newTraversed = Set.add currentComparable traversed
                yield current
                yield! current.GetType().GetFields(BindingFlags.Instance |||
                                                   BindingFlags.NonPublic |||
                                                   BindingFlags.Public)
                       |> Seq.collect (fun info -> info.GetValue(current)
                                                   |> inner newTraversed)
            }
            inner Set.empty root

        query {
            for o in traverseObjectGraph testFunc do
            let oType = o.GetType()
            where (oType.Assembly.FullName = asm.FullName)
            for m in oType.GetMethods() do
            where (m.Name = "Invoke" && m.DeclaringType = oType)
            select (Some (oType, m))
            headOrDefault
        }

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
            |> Seq.map (fun flatTest ->
                let (typeName, methodName) =
                    match getFuncTypeAndMethodToUse flatTest.test asm with
                    | None -> "Unknown", "Unknown"
                    | Some (t, m) -> t.FullName, m.Name
                new DiscoveryResult(flatTest.name, typeName, methodName))
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

