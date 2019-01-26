module Tests

open Expecto

[<Tests>]
let tests =
    testList "TestAdapter tests" [
        testCase "TestCases are fully qualified with dots from slashes" <| fun _ ->
            let name = "tests/hello/world"
            let case = TestCases.testCase "source" name
            Expect.equal case.FullyQualifiedName "tests.hello.world" "Should use '.' as separator"

        testCase "TestCases retain standard expecto name" <| fun _ ->
            let name = "tests/hello/world"
            let case = TestCases.testCase "source" name
            let caseName = TestCases.testName case
            Expect.equal caseName name "Should keep unmodified expecto test name"        
    ]