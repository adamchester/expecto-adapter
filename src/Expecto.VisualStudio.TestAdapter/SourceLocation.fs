namespace SourceLocation

open System.Linq
open System.Runtime.CompilerServices
open System.Collections.Generic
open Mono.Cecil.Cil
open Mono.Cecil
open Mono.Cecil.Rocks

type SourceLocation =
    {
        SourcePath: string
        LineNumber: int
    }

type SourceLocationFinder(assemblyPath:string) =
    let lineNumberIndicatingHiddenLine = 0xfeefee
    let getEcma335TypeName (clrTypeName:string) = clrTypeName.Replace("+", "/")

    let types =
        let readerParams = new ReaderParameters( ReadSymbols = true, InMemory = true )
        let moduleDefinition = ModuleDefinition.ReadModule(assemblyPath, readerParams)

        seq { for t in moduleDefinition.GetTypes() -> (t.FullName, t) }
        |> Map.ofSeq


    let getMethods typeName =
        match types.TryFind (getEcma335TypeName typeName) with
        | Some t ->
            Some (t.GetMethods())
        | _ -> None

    let optionFromObj = function 
        | null -> None 
        | x -> Some x

    let whenSome xs =
        seq
          {
            for x in xs do
                match x with
                | Some v -> yield v
                | None -> ()
          }

    let getSequencePoint(mapping: IDictionary<Instruction, SequencePoint>, instruction): SequencePoint =
        let (success, value) = mapping.TryGetValue instruction
        value

    let isStartDifferentThanHidden(seqPoint: SequencePoint) =
        if seqPoint = null then false
        else if seqPoint.StartLine <> lineNumberIndicatingHiddenLine then true
        else false

    let getFirstOrDefaultSequencePoint (m:MethodDefinition) =
        let mapping = m.DebugInformation.GetSequencePointMapping()
        
        m.Body.Instructions
        |> Seq.tryFind (fun i -> (getSequencePoint(mapping, i) |> isStartDifferentThanHidden))
        |> Option.map (fun i -> (getSequencePoint(mapping, i)))

    member this.getSourceLocation className methodName =
        match getMethods className with
        | Some methods ->
            let candidateSequencePoints =
                query
                  {
                    for m in methods do
                    where (m.Name = methodName)
                    select (getFirstOrDefaultSequencePoint m)
                  }
                |> whenSome
            query
              {
                for sp in candidateSequencePoints do
                sortBy sp.StartLine
                select { SourcePath = sp.Document.Url; LineNumber = sp.StartLine }
                take 1
              }
            |> Seq.tryFind (fun _ -> true)
        | _ -> None
