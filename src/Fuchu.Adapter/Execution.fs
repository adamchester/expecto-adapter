namespace Execution

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Reflection

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter

open Fuchu
open Fuchu.Impl
open System.Security
open System.Security.Permissions

open RemotingHelpers

type VsCallbackProxy(log: ITestExecutionRecorder, assemblyPath: string) =
    inherit MarshalByRefObjectInfiniteLease()

    member this.CaseStarted(name: string) =
        let testCase = new TestCase(name, Ids.ExecutorUri, assemblyPath)
        log.RecordStart(testCase)

    member this.CasePassed(name: string, duration: TimeSpan) =
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
        let testPrinters =
            {
                BeforeRun = vsCallback.CaseStarted
                Passed = (fun name duration -> vsCallback.CasePassed(name, duration))
                Ignored = (fun name message -> vsCallback.CaseSkipped(name, message))
                Failed = (fun name message duration -> vsCallback.CaseFailed(name, message, null, duration))
                Exception = (fun name ex duration -> vsCallback.CaseFailed(name, ex.Message, ex.StackTrace, duration))
            }
        let pmap (f: _ -> _) (s: _ seq) = s.AsParallel().Select(f) :> _ seq
//        let rec getTests parentName testList (test:Test) =
//            match test with
//            | TestCase tc ->
//                let cons x xs = seq { yield x; yield! xs }
//                cons (parentName, tc) testList
//            | TestList tl -> Seq.collect (getTests parentName testList) tl
//            | TestLabel (label, test) ->
//                let fullName = 
//                    if String.IsNullOrEmpty parentName
//                        then label
//                        else parentName + "/" + label
//                getTests fullName testList test
        let asm = Assembly.LoadFrom(assemblyPath)
        let tests =
            match testFromAssembly (asm) with
            | Some t -> t
            | None -> TestList []
        let testList =
            //let allTests = getTests "" Seq.empty tests
            let allTests = Fuchu.Test.toTestCodeList tests
            if testsToInclude = null then
                allTests
            else
                let requiredTests = testsToInclude |> HashSet
                allTests
                |> Seq.filter (fun (name, _) -> requiredTests.Contains(name))
        evalTestList testPrinters pmap testList
        |> ignore

type AssemblyExecutor(vsCallback: VsCallbackProxy, assemblyPath: string, testsToInclude: string[]) =
    let host = new TestAssemblyHost(assemblyPath)
    let proxy = host.CreateInAppdomain<ExecuteProxy>([|vsCallback; assemblyPath; testsToInclude|])
    interface IDisposable with
        member this.Dispose() =
            (host :> IDisposable).Dispose()
    member this.ExecuteTests() = proxy.ExecuteTests()


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

    interface ITestExecutor with
        member x.Cancel(): unit = 
            failwith "Not implemented yet"
        member x.RunTests(tests: IEnumerable<TestCase>, runContext: IRunContext, frameworkHandle: IFrameworkHandle): unit =
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
            try
            executors <-
                sources
                |> Seq.map
                    (fun assemblyPath ->
                        let callbackProxy = new VsCallbackProxy(frameworkHandle, assemblyPath)
                        new AssemblyExecutor(callbackProxy, assemblyPath, null))
                |> Array.ofSeq

            runAllExecutors ()
            with
            | x -> (frameworkHandle :> ITestExecutionRecorder).SendMessage(Logging.TestMessageLevel.Error, x.ToString())

