namespace Discovery

open System
open System.IO
open System.Reflection
open System.Security
open System.Security.Permissions

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging

open Fuchu
open Fuchu.Impl

open RemotingHelpers

type DiscoverProxy() =
    inherit MarshalByRefObjectInfiniteLease()
    member this.DiscoverTests(source: string) =
//        let rec getTests (test:Test) parentName testList =
//            match test with
//            | TestCase tc ->
//                 List.append testList [parentName]
//            | TestList tl ->
//                tl
//                |> Seq.map (fun test -> getTests test parentName testList)
//                |> List.concat
//            | TestLabel (label, test) ->
//                let fullName = 
//                    if String.IsNullOrEmpty parentName
//                        then label
//                        else parentName + "/" + label
//                getTests test fullName testList
        let asm = Assembly.LoadFrom(source)
        let tests =
            match testFromAssembly (asm) with
            | Some t -> t
            | None -> TestList []
        //getTests tests "" []
        Fuchu.Test.toTestCodeList tests
        |> Seq.map (fun (name, _) -> name)
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
                logger.SendMessage(Logging.TestMessageLevel.Informational, System.AppDomain.CurrentDomain.BaseDirectory)
                for source in sources do
                    use host = new TestAssemblyHost(source)
//                    let assemblyFullPath = Path.GetFullPath(source)
//                    let configFullPath = assemblyFullPath + ".config";
//                    let configFullPath =
//                        if File.Exists(configFullPath)
//                            then configFullPath
//                            else null
//                    let setup =
//                        new AppDomainSetup
//                            (ApplicationBase = Path.GetDirectoryName(assemblyFullPath),
//                             ApplicationName = Guid.NewGuid().ToString(),
//                             ConfigurationFile = configFullPath)
//                    let appDomain = AppDomain.CreateDomain(setup.ApplicationName, null, setup, new PermissionSet(PermissionState.Unrestricted))
//                    try
//                        let discoverProxy = appDomain.CreateInstanceAndUnwrap((typeof<DiscoverProxy>).Assembly.FullName, typeof<DiscoverProxy>.FullName) :?> DiscoverProxy
                    let discoverProxy = host.CreateInAppdomain<DiscoverProxy>()
                    let testList = discoverProxy.DiscoverTests(source)
                    for test in testList do
                        discoverySink.SendTestCase(new TestCase(test, Ids.ExecutorUri, source))
//                    finally
//                        AppDomain.Unload(appDomain)
            with
            | x -> logger.SendMessage(Logging.TestMessageLevel.Error, x.ToString())

