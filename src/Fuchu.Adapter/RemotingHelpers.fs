namespace RemotingHelpers

open System
open System.IO
open System.Runtime.Remoting
open System.Reflection
open System.Security
open System.Security.Permissions

type MarshalByRefObjectInfiniteLease() =
    inherit MarshalByRefObject()
        override this.InitializeLifetimeService() : obj = null
    interface IDisposable with
        member this.Dispose() = ignore <| RemotingServices.Disconnect(this)


type TestAssemblyHost(source) =
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

    member this.CreateInAppdomain<'P when 'P :> MarshalByRefObject and 'P : (new : unit -> 'P)>() =
        appDomain.CreateInstanceAndUnwrap((typeof<'P>).Assembly.FullName, typeof<'P>.FullName) :?> 'P


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
