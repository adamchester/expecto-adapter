# expecto-adapter
Visual Studio test adapter for Expecto

This adapter enable tests based on the [Expecto] (https://github.com/haf/Expecto) F# test framework to show up in Visual Studio's **Text Explorer** window, and also in Visual Studio Online builds.

This was originally developed as the [Fuchu Adapter](https://github.com/interactsw/fuchu-adapter).

## Usage

If you're using [paket](https://fsprojects.github.io/Paket/):
 1. add `nuget Expecto.VisualStudio.TestAdapter version_in_path: true` to your `paket.dependencies` file
 1. add `Expecto.VisualStudio.TestAdapter` to your `paket.references` file in the Exe test project.
 1. run `paket install`, then in the same folder as your test executable project, 

note: paket might generate a `packages.config` automatically for you. add it to your test exe project file.

![image](https://cloud.githubusercontent.com/assets/570470/23829702/b08a9924-0744-11e7-910f-fb9fd06789d6.png)

If you are using NuGet package manager, just use NuGet to add [Expecto.VisualStudio.TestAdapter] (https://www.nuget.org/packages/Expecto.VisualStudio.TestAdapter/) to your Exe test project.

![image](https://cloud.githubusercontent.com/assets/570470/23829697/97c5f10e-0744-11e7-91e6-f8b0ebfa7bf2.png)

In both cases, Visual Studio should read the `packages.config` and detect the presense of the test adapter, and then will use it to populate the Test Explorer and run tests.
