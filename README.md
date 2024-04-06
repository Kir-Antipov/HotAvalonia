# HotAvalonia

[![GitHub Build Status](https://img.shields.io/github/actions/workflow/status/Kir-Antipov/HotAvalonia/build.yml?style=flat&logo=github&cacheSeconds=3600)](https://github.com/Kir-Antipov/HotAvalonia/actions/workflows/build.yml)
[![Version](https://img.shields.io/github/v/release/Kir-Antipov/HotAvalonia?sort=date&style=flat&label=version&cacheSeconds=3600)](https://github.com/Kir-Antipov/HotAvalonia/releases/latest)
[![License](https://img.shields.io/github/license/Kir-Antipov/HotAvalonia?style=flat&cacheSeconds=36000)](https://github.com/Kir-Antipov/HotAvalonia/blob/HEAD/LICENSE.md)

<img alt="HotAvalonia Icon" src="https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/icon.png" width="128">

`HotAvalonia` is a .NET library crafted to seamlessly integrate hot reload functionality into Avalonia applications. Acting as a transformative tool for Avalonia developers, it enables dynamic and instantaneous updates to XAML without the need for full recompilation. This empowers developers to witness UI changes in real-time, accelerating the design and development workflow.

----

## NuGet Packages

| **Package** | **Latest Version** |
|:------------|:-------------------|
| HotAvalonia | [![NuGet](https://img.shields.io/nuget/v/HotAvalonia?style=flat&logo=nuget&label=nuget&cacheSeconds=3600)](https://nuget.org/packages/HotAvalonia/ "Download HotAvalonia from NuGet.org") |
| HotAvalonia.Extensions | [![NuGet](https://img.shields.io/nuget/v/HotAvalonia.Extensions?style=flat&logo=nuget&label=nuget&cacheSeconds=3600)](https://nuget.org/packages/HotAvalonia.Extensions/ "Download HotAvalonia.Extensions from NuGet.org") |

----

## Getting Started

### Installation

To get started, you'll need to add the following three packages to your project:

 - [Avalonia.Markup.Xaml.Loader](https://nuget.org/packages/Avalonia.Markup.Xaml.Loader/) - The official Avalonia package responsible for runtime XAML parsing.
 - [HotAvalonia](https://nuget.org/packages/HotAvalonia/) - The package that integrates hot reload functionality into your application.
 - [HotAvalonia.Extensions](https://nuget.org/packages/HotAvalonia.Extensions/) - The package that provides `.EnableHotReload()` and `.DisableHotReload()` extension methods for greater convenience.

While you could use the `dotnet add` command to accomplish this, I would strongly recommend a more manual yet flexible approach - insert the following snippet into your `.csproj` file:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>$(DefineConstants);ENABLE_XAML_HOT_RELOAD</DefineConstants>
</PropertyGroup>

<ItemGroup>
  <PackageReference Condition="$(DefineConstants.Contains(ENABLE_XAML_HOT_RELOAD))" Include="Avalonia.Markup.Xaml.Loader" Version="$(AvaloniaVersion)" />
  <PackageReference Condition="$(DefineConstants.Contains(ENABLE_XAML_HOT_RELOAD))" Include="HotAvalonia" Version="1.0.1" />
  <PackageReference Include="HotAvalonia.Extensions" Version="1.0.1" PrivateAssets="All" />
</ItemGroup>
```

Make sure to replace `$(AvaloniaVersion)` with the version of Avalonia you're currently using; in other words, `Avalonia.Markup.Xaml.Loader` should match the main `Avalonia` package's version. You may also update `HotAvalonia` and `HotAvalonia.Extensions` to their latest compatible versions.

In the snippet above, we introduce a new preprocessor directive - `ENABLE_XAML_HOT_RELOAD`. It is responsible for activating hot reload capabilities. Here the directive is defined whenever the project is compiled using the Debug configuration, but you can set your own conditions for its activation. Also, if you wish to deactivate hot reload even when `ENABLE_XAML_HOT_RELOAD` might be present, define `DISABLE_XAML_HOT_RELOAD`, which will override the former directive.

Next, we reference the necessary packages mentioned above. `Avalonia.Markup.Xaml.Loader` and `HotAvalonia` are required only when hot reload is active, so they are included solely when the `ENABLE_XAML_HOT_RELOAD` directive is present. `HotAvalonia.Extensions` is unique in that matter, since it provides the methods we need always to be accessible, so we just mark it as a development-only dependency.

This setup **guarantees** that no hot reload logic will infiltrate the production version of your app. All calls to `HotAvalonia` will be automatically and completely eradicated from the Release builds, as can be seen below:

| Debug | Release |
| :---: | :-----: |
| ![Debug build](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/build_debug.png) | ![Release build](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/build_release.png) |

### Usage

Once you have installed all the necessary dependencies, it's time to unlock the hot reload capabilities for your app. Fortunately, this process is quite straightforward!

Typically, the code of the main application class (`App.axaml.cs` / `App.xaml.cs`) looks something like this:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HotReloadDemo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ...
    }
}
```

To enable hot reload, all you need to do is import the `HotAvalonia` namespace and use the `.EnableHotReload()` extension method on the `App` instance **before** the `AvaloniaXamlLoader.Load(this)` call:

```diff
  using Avalonia;
  using Avalonia.Controls.ApplicationLifetimes;
  using Avalonia.Markup.Xaml;
+ using HotAvalonia;

  namespace HotReloadDemo;

  public partial class App : Application
  {
      public override void Initialize()
      {
+         this.EnableHotReload(); // Ensure this line **precedes** `AvaloniaXamlLoader.Load(this);`
          AvaloniaXamlLoader.Load(this);
      }

      public override void OnFrameworkInitializationCompleted()
      {
          // ...
      }
  }
```

With this setup, you can debug the app using your IDE's built-in debugger, run the project with `dotnet run`, combine `dotnet watch` hot reload capabilities with XAML hot reload provided by HotAvalonia, or simply build the app using `dotnet build` and run it as a standalone executable. Either way, you can expect your app to reload whenever one of the source files of your controls changes.

If you ever need to temporarily disable hot reload while the app is running, you can call `Application.Current.DisableHotReload()`. To resume hot reload, simply call `.EnableHotReload()` on `Application.Current` again.

----

## Examples

Here are some examples that demonstrate HotAvalonia in action:

| ![Hot Reload: App](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_app.gif) | ![Hot Reload: User Control](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_user_control.gif) |
| :---: | :-----: |
| ![Hot Reload: View](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_view.gif) | ![Hot Reload: Styles](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_styles.gif) |
| ![Hot Reload: Resources](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_resources.gif) | ![Hot Reload: Window](https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/examples/hot_reload_window.gif) |

To try it out yourself, you can run the [`samples/HotReloadDemo`](https://github.com/Kir-Antipov/HotAvalonia/blob/HEAD/samples/HotReloadDemo) application included in the repository.

----

## Limitations

While HotAvalonia is a powerful tool for enhancing your Avalonia development workflow, it does have some limitations to keep in mind:

 1. **Code Files (.cs):** HotAvalonia cannot process `.cs` files *(or any other code files for that matter)*. Therefore, when you create a new control, typically defined as a pair of `.axaml` and `.axaml.cs` files, you will need to recompile the project to see the changes take effect. However, existing XAML files can be edited freely without recompilation.

 2. **New Assets:** HotAvalonia does not watch for new assets *(such as icons)* being added to the `Assets` folder. To make newly added assets visible in your application, you will need to recompile the project.

 3. **.NET 7+ Compatibility:** It's important to note that .NET 7 introduced breaking changes that affect some internal mechanisms we have been relying on for years. To ensure the full hot reload experience, it is recommended to run your application with a debugger attached to it, or in the `dotnet watch` mode. This is necessary as, without these conditions met, certain resources *(like styles or resource dictionaries)* may not hot reload correctly *(do not ask why, it is what it is)*. If you are still using .NET 6 or an earlier version, these additional requirements do not apply.

 4. **Referenced Projects:** Currently, HotAvalonia does not watch for controls located in referenced projects. In other words, hot reload only works for controls defined within the entry assembly. While this limitation exists, it is technically feasible to implement support for this feature in the future.

 5. **`dotnet watch`:** In rare edge cases, when using HotAvalonia in conjunction with `dotnet watch`, Avalonia may not be able to discover some newly added class members *(such as event handlers in views)*. However, your hot reload experience should generally be quite smooth when editing view models.

 6. **Mobile Development:** Unlike in a local development environment, where your application and project files share the same filesystem, in an emulator, your application is running on what effectively is a remote system. To enable hot reloading there, your project must be accessible by the emulator. This can be achieved by mounting the directory you are working with as a remote filesystem on the emulated device. With this setup, hot reload on emulators should work, but there may be additional challenges yet to be discovered. Feel free to open an issue if you've stumbled upon any!

----

## License

Licensed under the terms of the [MIT License](https://github.com/Kir-Antipov/HotAvalonia/blob/HEAD/LICENSE.md).
