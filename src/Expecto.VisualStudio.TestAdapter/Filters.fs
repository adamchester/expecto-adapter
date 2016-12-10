module Filters

open System.IO
open System.Collections.Generic

let sourcesUsingExpecto (sources:IEnumerable<string>) =
    query
      {
        for source in sources do
        where ((Path.GetFileName(source) <> "Expecto.VisualStudio.TestAdapter.dll") &&
               (File.Exists(Path.Combine(Path.GetDirectoryName(source), "Expecto.dll"))))
      }
