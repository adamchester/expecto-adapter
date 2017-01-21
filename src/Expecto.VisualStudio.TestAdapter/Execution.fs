namespace Execution

open System
open System.Collections.Generic
open System.Diagnostics
open System.Linq
open System.Reflection

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging

open Newtonsoft.Json

open Expecto
open Expecto.Impl

open Filters
open RemotingHelpers

        
type TestExecutionRecorderProxy(recorder:ITestExecutionRecorder, assemblyPath:string) =
    inherit MarshalByRefObjectInfiniteLease()
    interface IObserver<string * string> with
        member this.OnCompleted(): unit = 
            ()
        member x.OnError(error: exn): unit = 
            recorder.SendMessage(TestMessageLevel.Error, error.ToString())
        member x.OnNext((messageType, message): string * string): unit = 
            match messageType with
            | "LogInfo" -> recorder.SendMessage(TestMessageLevel.Informational, message)
            | "CaseStarted" ->
                let testCase = new TestCase(message, Ids.ExecutorUri, assemblyPath)
                recorder.RecordStart(testCase)
            | "CasePassed" ->
                let d = JsonConvert.DeserializeObject<Dictionary<string, string>>(message)
                let name = d.["name"]
                let duration = TimeSpan.ParseExact(d.["duration"], "c", System.Globalization.CultureInfo.InvariantCulture)
                let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
                let result =
                    new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult
                        (testCase,
                         DisplayName = name,
                         Outcome = TestOutcome.Passed,
                         Duration = duration,
                         ComputerName = Environment.MachineName)
                recorder.RecordResult(result)
                recorder.RecordEnd(testCase, result.Outcome)
            | "CaseFailed" ->
                let d = JsonConvert.DeserializeObject<Dictionary<string, string>>(message)
                let name = d.["name"]
                let message = d.["message"]
                let stackTrace = d.["stackTrace"]
                let duration = TimeSpan.ParseExact(d.["duration"], "c", System.Globalization.CultureInfo.InvariantCulture)
                let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
                let result =
                    new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult
                        (testCase,
                         DisplayName = name,
                         ErrorMessage = message,
                         ErrorStackTrace = stackTrace,
                         Outcome = TestOutcome.Failed,
                         Duration = duration,
                         ComputerName = Environment.MachineName)
                recorder.RecordResult(result)
                recorder.RecordEnd(testCase, result.Outcome)
            | "CaseSkipped" ->
                let d = JsonConvert.DeserializeObject<Dictionary<string, string>>(message)
                let name = d.["name"]
                let message = d.["message"]
                let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
                let result =
                    new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult
                        (testCase,
                         DisplayName = name,
                         ErrorMessage = message,
                         Outcome = TestOutcome.Skipped,
                         ComputerName = Environment.MachineName)
                recorder.RecordResult(result)
                recorder.RecordEnd(testCase, result.Outcome)
            | _ -> recorder.SendMessage(TestMessageLevel.Error, Printf.sprintf "Expecto.VisualStudio.TestAdapter internal error - recorder proxy received unknown message type: %s" messageType)
        

type VsCallbackForwarder(observer: IObserver<string * string>, assemblyPath: string) =
    inherit MarshalByRefObjectInfiniteLease()

    let testEnded (messageType, values: (string*string) list) =
        let textStream = new System.IO.MemoryStream()
        let d = values.ToDictionary((fun (k, _) -> k), (fun (_, v) -> v))
        let text = JsonConvert.SerializeObject(d)
        observer.OnNext((messageType, text))
        
    member this.LogInfo (message:string) =
        observer.OnNext(("LogInfo", message))

    member this.CaseStarted(name: string) =
        observer.OnNext("CaseStarted", name)

    member this.CasePassed(name: string, duration: TimeSpan) =
        testEnded ("CasePassed", ["name", name; "duration", duration.ToString("c")])

    member this.CaseFailed(name: string, message: string, stackTrace: string, duration: TimeSpan) =
        testEnded ("CaseFailed", ["name", name; "message", message; "stackTrace", stackTrace; "duration", duration.ToString("c")])

    member this.CaseSkipped(name: string, message: string) =
        testEnded ("CaseSkipped", ["name", name; "message", message])


// The curious 1-tuple argument here is so that we can force the AppDomain class to locate the correct
// constructor. It enables us to pass in an argument that is unambiguously typed as the required
// interface. (Otherwise, it ends up being of type TestExecutionRecorderProxy, and the dynamic
// creation attempt seems not to be able to work out that it needs the constructor that takes an
// IObserver<string*string>)
type ExecuteProxy(proxyHandler: Tuple<IObserver<string * string>>, assemblyPath: string, testsToInclude: string[]) =
    inherit MarshalByRefObjectInfiniteLease()
    let vsCallback: VsCallbackForwarder = new VsCallbackForwarder(proxyHandler.Item1, assemblyPath)
    member this.ExecuteTests() =
        if testsToInclude = null then
            vsCallback.LogInfo(sprintf "Executing all tests in %s" assemblyPath)
        else
            vsCallback.LogInfo(sprintf "Executing tests from: %s. %d tests (%s)" assemblyPath testsToInclude.Length (String.Join(",", testsToInclude)))

        // Expecto invokes callbacks regarding test execution to what it
        let testPrinters =
            {
                beforeRun = (fun test -> async { vsCallback.CaseStarted (test.ToString()) })
                beforeEach = (fun name -> async { vsCallback.LogInfo(sprintf "starting '%s'" name) })
                info = (fun info -> async { vsCallback.LogInfo(info)}) 
                summary = (fun results -> async { vsCallback.LogInfo(sprintf "summary %A" results)})
                passed = (fun name duration -> async {vsCallback.CasePassed(name, duration)})
                ignored = (fun name message -> async {vsCallback.CaseSkipped(name, message)})
                failed = (fun name message duration -> async {vsCallback.CaseFailed(name, message, null, duration)})
                exn = (fun name ex duration -> async {vsCallback.CaseFailed(name, ex.Message, ex.StackTrace, duration)})
            }
        let asm = Assembly.LoadFrom(assemblyPath)
        if not (asm.GetReferencedAssemblies().Any(fun a -> a.Name = "Expecto")) then
            vsCallback.LogInfo(sprintf "Skipping: %s because it does not reference Expecto" assemblyPath)
        else            
            let tests = match testFromAssembly (asm) with
                        | Some t -> t
                        | None -> TestList ([], Normal)

            let testList = Expecto.Test.toTestCodeList tests;
            
            vsCallback.LogInfo(sprintf "All tests: %d" (testList.Count()))

            let includedTestNames = (testsToInclude |> HashSet)
            
            let testsToRun = match testsToInclude with
                             | null -> tests
                             | _ -> tests |> Expecto.Test.filter (fun testName -> includedTestNames.Contains(testName))
            
            vsCallback.LogInfo(sprintf "Number of tests included: %d" ((testList |> List.filter (fun tc -> includedTestNames.Contains(tc.name))).Count()))
            
            let conf = { defaultConfig with printer = testPrinters }
            evalPar conf testsToRun |> ignore

type AssemblyExecutor(proxyHandler: IObserver<string * string>, assemblyPath: string, testsToInclude: string[]) =
    let host = new TestAssemblyHost(assemblyPath)
    let wrappedArg: Tuple<IObserver<string*string>> = Tuple.Create(proxyHandler)
    let proxy = host.CreateInAppdomain<ExecuteProxy>([|wrappedArg; assemblyPath; testsToInclude|])
    interface IDisposable with
        member this.Dispose() =
            (host :> IDisposable).Dispose()
    member this.ExecuteTests() =
        proxyHandler.OnNext("LogInfo", "Executing tests in assembly " + assemblyPath)
        proxy.ExecuteTests()

[<ExtensionUri(Ids.ExecutorId)>]
type ExpectoTestExecutor() =
    let mutable (executors:AssemblyExecutor []) = null

    let runAllExecutors () =
        executors
        |> Seq.map
            (fun executor -> async { executor.ExecuteTests() })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore
        for executor in executors do
            (executor :> IDisposable).Dispose()
        executors <- null

    interface ITestExecutor with
        member x.Cancel(): unit = 
            failwith "Not implemented yet"
        member x.RunTests(tests: IEnumerable<TestCase>, runContext: IRunContext, frameworkHandle: IFrameworkHandle): unit =
            (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Informational, "Running selected tests")
            try
                let testsByAssembly =
                    query
                      {
                        for testCase in tests do
                        groupBy testCase.Source
                      }
                executors <-
                    testsByAssembly
                    |> Seq.map
                        (fun testGroup ->
                            let assemblyPath = testGroup.Key
                            let callbackProxy:IObserver<string*string> = new TestExecutionRecorderProxy(frameworkHandle, assemblyPath) :> IObserver<string*string>
                            let testNames =
                                query
                                  {
                                    for test in testGroup do
                                    select test.FullyQualifiedName
                                  }
                                |> Array.ofSeq
                            new AssemblyExecutor(callbackProxy, assemblyPath, testNames))
                    |> Array.ofSeq

                runAllExecutors ()
            with
            | x -> (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Error, x.ToString())

        member x.RunTests(sources: IEnumerable<string>, runContext: IRunContext, frameworkHandle: IFrameworkHandle): unit =
            (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Informational, sprintf "Running all tests (%s)" (FileVersionInfo.GetVersionInfo(typeof<ExpectoTestExecutor>.Assembly.Location).FileVersion))
            try
                executors <-
                    sourcesUsingExpecto sources
                    |> Seq.map
                        (fun assemblyPath ->
                            let callbackProxy:IObserver<string*string> = new TestExecutionRecorderProxy(frameworkHandle, assemblyPath) :> IObserver<string*string>
                            (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Informational, ("Creating test host for " + assemblyPath))
                            new AssemblyExecutor(callbackProxy, assemblyPath, null))
                    |> Array.ofSeq

                runAllExecutors ()
            with
            | x -> (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Error, x.ToString())

