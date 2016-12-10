# expecto-adapter
Visual Studio test adapter for Expecto

This adapter enable tests based on the [Expecto] (https://github.com/haf/Expecto) F# test framework to show up in Visual Studio's **Text Explorer** window, and also in Visual Studio Online builds.

This was originally developed as the [Fuchu Adapter](https://github.com/interactsw/fuchu-adapter).

## Usage

Use the NuGet package manager to add a reference to the [Expecto.VisualStudio.TestAdapter] (https://www.nuget.org/packages/Expecto.VisualStudio.TestAdapter/) package to a test project that uses Expecto. (There is no need to install anything globally.) Visual Studio will detect the presence of the test adapter, and will use it to populate the Test Explorer and run tests.
