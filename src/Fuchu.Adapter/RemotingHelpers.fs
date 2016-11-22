namespace RemotingHelpers

open System
open System.IO
open System.Runtime.Remoting
open System.Reflection
open System.Security
open System.Security.Permissions


/// <summary>
/// Base class for objects accessible across AppDomain boundaries with
/// no lease timeout.
/// </summary>
/// <remarks>
/// We need to execute tests for each test assembly in an AppDomain created
/// with that assembly's location as a codebase, to ensure that we pick up
/// configuration settings such as assembly binding redirects. We therefore
/// need objects capable of cross-AppDomain usage, which means we need to
/// use .NET Remoting, which in turn requires the objects at the boundary
/// to derive from MarshalByRefObject. However, by default objects used
/// in this way only work for as long as their 'lease' lives. This is a
/// mechanism designed to avoid leaking remote objects when you lose network
/// connectivity, but it's unhelpful for intra-process cross-AppDomain
/// communication - it just means that objects become inaccessible after
/// a certain length of time. So this base class extends the lease to be
/// indefinite, preventing early removal.
/// </remarks>
type MarshalByRefObjectInfiniteLease() =
    inherit MarshalByRefObject()
        override this.InitializeLifetimeService() : obj = null
    interface IDisposable with
        member this.Dispose() = ignore <| RemotingServices.Disconnect(this)

/// <summary>
/// Provides an AppDomain for a particular test assembly.
/// </summary>
/// <remarks>
/// We create an AppDomain for each test, with the codebase set to the
/// assembly's location to ensure that assemblies are resolved correctly.
/// </remarks>
type TestAssemblyHost(source) =
    // In some circumstances, our appdomain seems to end up being unable to resolve this assembly.
    // So I've used the technique Rick Strahl describes in
    //  https://weblog.west-wind.com/posts/2009/Jan/19/Assembly-Loading-across-AppDomains
    // It's not clear why this is necessary - it seems the Assembly.Load we try as a first resort
    // works, so you'd think it would just work without custom resolution logic. But there we are.
    static do
        let CurrentDomain_AssemblyResolve =
            fun (sender:obj) (args:ResolveEventArgs) ->
                let assembly = 
                    try
                        System.Reflection.Assembly.Load(args.Name);
                    with
                    | _ -> null // ignore load error
                if assembly <> null then
                    assembly
                else
                    let parts = args.Name.Split(',')
                    let file = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\" + parts.[0].Trim() + ".dll"
                    Printf.printf "Normal resolution failed. Trying %s\n" file
                    System.Reflection.Assembly.LoadFrom(file)
        AppDomain.CurrentDomain.add_AssemblyResolve(new ResolveEventHandler(CurrentDomain_AssemblyResolve))

    let mutable appDomain =
        let setup =
            let assemblyFullPath = Path.GetFullPath(source)
            let configFullPath = assemblyFullPath + ".config";
            let configFullPath =
                if File.Exists(configFullPath)
                    then configFullPath
                    else null
            new AppDomainSetup
                (ApplicationBase = Path.GetDirectoryName(assemblyFullPath),
                 ApplicationName = Guid.NewGuid().ToString(),
                 ConfigurationFile = configFullPath)
        AppDomain.CreateDomain(setup.ApplicationName, null, setup, new PermissionSet(PermissionState.Unrestricted))

    interface IDisposable with
        member this.Dispose() =
            if appDomain <> null then
                AppDomain.Unload(appDomain)
                appDomain <- null

    /// <summary>
    /// Create an instance of the specified type in the test AppDomain.
    /// </summary>
    member this.CreateInAppdomain<'P when 'P :> MarshalByRefObject and 'P : (new : unit -> 'P)>() =
        appDomain.CreateInstanceAndUnwrap((typeof<'P>).Assembly.FullName, typeof<'P>.FullName) :?> 'P

    /// <summary>
    /// Create an instance of the specified type in the test AppDomain, using the
    /// constructor arguments supplied.
    /// </summary>
    member this.CreateInAppdomain<'P when 'P :> MarshalByRefObject>(args:obj []) =
        appDomain.CreateInstanceAndUnwrap(
            (typeof<'P>).Assembly.FullName,
            typeof<'P>.FullName,
            false,
            BindingFlags.Default,
            null,
            args,
            null,
            null) :?> 'P
