# fuchu-adapter
Visual Studio test adapter for Fuchu

This adapter enable tests based on the [Fuchu] (https://github.com/mausch/Fuchu) F# test framework to show up in Visual Studio's **Text Explorer** window, and also in Visual Studio Online builds.

Note that this project is not affiliated with the main Fuchu project. It is an independent project, developed because I wanted to use Fuchu in conjunction with Visual Studio tooling.

## Usage

Use the NuGet package manager to add a reference to the [Fuchu.Adapter] (https://www.nuget.org/packages/Fuchu.Adapter/) package to a test project that uses Fuchu. (There is no need to install anything globally.) Visual Studio will detect the presence of the test adapter, and will use it to populate the Test Explorer and run tests.
