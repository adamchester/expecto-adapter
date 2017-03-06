module Expecto.Sample

open Expecto
open Expecto.ExpectoFsCheck
open FsCheck

[<Tests>]
let tests =
  testList "samples" [
    testCase "universe exists" <| fun _ ->
      let subject = true
      Expect.isTrue subject "I compute, therefore I am."

    testCase "should fail" <| fun _ ->
      let subject = false
      Expect.isTrue subject "I should fail because the subject is false."

    testProperty "Addition is commutative" <| fun a b ->
      a + b = b + a
  ]

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssembly defaultConfig argv
