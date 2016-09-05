namespace Execution

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Reflection

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging

open Fuchu
open Fuchu.Impl
open System.Security
open System.Security.Permissions

open Filters
open RemotingHelpers

type VsCallbackProxy(log: ITestExecutionRecorder, assemblyPath: string) =
    inherit MarshalByRefObjectInfiniteLease()

    member this.LogInfo (message:string) =
        log.SendMessage(TestMessageLevel.Informational, message)

    member this.CaseStarted(name: string) =
        log.SendMessage(TestMessageLevel.Informational, "CaseStarted: " + name)
        let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
        log.RecordStart(testCase)

    member this.CasePassed(name: string, duration: TimeSpan) =
        log.SendMessage(TestMessageLevel.Informational, "CasePassed: " + name)
        let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
        let result =
            new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult
                (testCase,
                 DisplayName = name,
                 Outcome = TestOutcome.Passed,
                 Duration = duration,
                 ComputerName = Environment.MachineName)
        log.RecordResult(result)
        log.RecordEnd(testCase, TestOutcome.Passed)

    member this.CaseFailed(name: string, message: string, stackTrace: string, duration: TimeSpan) =
        log.SendMessage(TestMessageLevel.Informational, "CaseFailed: " + name)
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
        log.RecordResult(result)
        log.RecordEnd(testCase, TestOutcome.Failed)

    member this.CaseSkipped(name: string, message: string) =
        log.SendMessage(TestMessageLevel.Informational, "CaseSkipped: " + name)
        let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
        let result =
            new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult
                (testCase,
                 DisplayName = name,
                 ErrorMessage = message,
                 Outcome = TestOutcome.Skipped,
                 ComputerName = Environment.MachineName)
        log.RecordResult(result)
        log.RecordEnd(testCase, TestOutcome.Skipped)


type ExecuteProxy(vsCallback: VsCallbackProxy, assemblyPath: string, testsToInclude: string[]) =
    inherit MarshalByRefObjectInfiniteLease()
    member this.ExecuteTests() =
        if testsToInclude = null then
            vsCallback.LogInfo(sprintf "ExecuteProxy.ExecuteTests: %s, all tests" assemblyPath)
        else
            vsCallback.LogInfo(sprintf "ExecuteProxy.ExecuteTests: %s, %d tests (%s)" assemblyPath testsToInclude.Length (String.Join(",", testsToInclude)))

        // Fuchu invokes callbacks regarding test execution to what it
        //
        let testPrinters =
            {
                BeforeRun = vsCallback.CaseStarted
                Passed = (fun name duration -> vsCallback.CasePassed(name, duration))
                Ignored = (fun name message -> vsCallback.CaseSkipped(name, message))
                Failed = (fun name message duration -> vsCallback.CaseFailed(name, message, null, duration))
                Exception = (fun name ex duration -> vsCallback.CaseFailed(name, ex.Message, ex.StackTrace, duration))
            }
        let asm = Assembly.LoadFrom(assemblyPath)
        let tests =
            match testFromAssembly (asm) with
            | Some t -> t
            | None -> TestList []
        let testList =
            let allTests = Fuchu.Test.toTestCodeList tests
            vsCallback.LogInfo(sprintf "All tests: %d" (allTests.Count()))
            if testsToInclude = null then
                allTests
            else
                let requiredTests = testsToInclude |> HashSet
                allTests
                |> Seq.filter (fun (name, _) -> requiredTests.Contains(name))
        vsCallback.LogInfo(sprintf "Number of tests included: %d" (testList.Count()))
        let pmap (f: _ -> _) (s: _ seq) = s.AsParallel().Select(f) :> _ seq
        evalTestList testPrinters pmap testList
        //evalTestList testPrinters Seq.map testList
        |> Seq.toList   // Force evaluation
        |> ignore

type AssemblyExecutor(vsCallback: VsCallbackProxy, assemblyPath: string, testsToInclude: string[]) =
    let host = new TestAssemblyHost(assemblyPath)
    let proxy = host.CreateInAppdomain<ExecuteProxy>([|vsCallback; assemblyPath; testsToInclude|])
    interface IDisposable with
        member this.Dispose() =
            (host :> IDisposable).Dispose()
    member this.ExecuteTests() =
        vsCallback.LogInfo ("Executing tests in assembly " + assemblyPath)
        proxy.ExecuteTests()


[<ExtensionUri(Ids.ExecutorId)>]
type FuchuTestExecutor() =
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
                            let callbackProxy = new VsCallbackProxy(frameworkHandle, assemblyPath)
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
            (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Informational, "Running all tests")
            try
                executors <-
                    sourcesUsingFuchu sources
                    |> Seq.map
                        (fun assemblyPath ->
                            let callbackProxy = new VsCallbackProxy(frameworkHandle, assemblyPath)
                            (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Informational, ("Creating test host for " + assemblyPath))
                            new AssemblyExecutor(callbackProxy, assemblyPath, null))
                    |> Array.ofSeq

                runAllExecutors ()
            with
            | x -> (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Error, x.ToString())

