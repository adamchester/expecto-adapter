module TestCases

open System.Linq
open Microsoft.VisualStudio.TestPlatform.ObjectModel

let private fullyQualified (name:string) =
    //Expecto uses / instead of . as separator
    name.Replace("/", ".")

let private traitName = "ExpectoTestName"

let testCase source name =
    let tc = new TestCase(fullyQualified name, Ids.ExecutorUri, source)
    tc.Traits.Add(new Trait(traitName, name))
    tc

let testName (testCase: TestCase) =
    (testCase.Traits.First (fun s -> s.Name = traitName)).Value