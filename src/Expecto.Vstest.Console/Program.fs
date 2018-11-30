module Expecto.Sample

open Expecto
open Expecto.ExpectoFsCheck
open FsCheck

type UserGen =
  static member NegativeInt32() =
    Arb.Default.Int32()
    |> Arb.mapFilter (fun x -> -abs x) (fun x -> x < 0)

let config = { FsCheckConfig.defaultConfig with arbitrary = [ typeof<UserGen> ] }

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

    testPropertyWithConfig config "Can set config arbitrary" <| fun a ->
      a < 0
  ]

[<EntryPoint>]
let main argv =
  runTestsInAssembly defaultConfig argv
