namespace Fuchu.Adapter.AssemblyInfo

open System.Reflection
//open System.Runtime.CompilerServices
//open System.Runtime.InteropServices

#if DEBUG
[<assembly: AssemblyConfiguration("Debug")>]
#else
[<assembly: AssemblyConfiguration("Release")>]
#endif

[<assembly: AssemblyCompany("Interact Software Ltd.")>]
[<assembly: AssemblyProduct("Flyntax")>]
[<assembly: AssemblyCopyright("Copyright © 2016 Ian Griffiths")>]
[<assembly: AssemblyTrademark("")>]

// Note that in automated builds, the AssemblyFileVersion and AssemblyVersion
// are updated by a script.
[<assembly: AssemblyFileVersion("9999.0.0.0")>]
[<assembly: AssemblyVersion("9999.0.0.0")>]

do
    ()