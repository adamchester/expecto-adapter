module Filters

open System.IO
open System.Collections.Generic

let sourcesUsingFuchu (sources:IEnumerable<string>) =
    query
        {
        for source in sources do
        where (File.Exists(Path.Combine(Path.GetDirectoryName(source), "Fuchu.dll")))
        }
