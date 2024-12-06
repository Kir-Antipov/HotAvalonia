# HotAvalonia

[![GitHub Build Status](https://img.shields.io/github/actions/workflow/status/Kir-Antipov/HotAvalonia/build.yml?logo=github)](https://github.com/Kir-Antipov/HotAvalonia/actions/workflows/build.yml)
[![Version](https://img.shields.io/github/v/release/Kir-Antipov/HotAvalonia?sort=date&label=version)](https://github.com/Kir-Antipov/HotAvalonia/releases/latest)
[![License](https://img.shields.io/github/license/Kir-Antipov/HotAvalonia?cacheSeconds=36000)](https://github.com/Kir-Antipov/HotAvalonia/blob/HEAD/LICENSE.md)

<img alt="HotAvalonia Icon" src="https://raw.githubusercontent.com/Kir-Antipov/HotAvalonia/HEAD/media/icon.png" width="128">

`HotAvalonia` is a .NET library crafted to seamlessly integrate hot reload functionality into Avalonia applications. Acting as a transformative tool for Avalonia developers, it enables dynamic and instantaneous updates to XAML without the need for full recompilation. This empowers developers to witness UI changes in real-time, accelerating the design and development workflow.

----

## NuGet Packages

| **Package** | **Latest Version** |
|:------------|:-------------------|
| HotAvalonia | [![NuGet](https://img.shields.io/nuget/v/HotAvalonia?logo=nuget&label=nuget)](https://nuget.org/packages/HotAvalonia/ "Download HotAvalonia from NuGet.org") |
| HotAvalonia.Extensions | [![NuGet](https://img.shields.io/nuget/v/HotAvalonia.Extensions?logo=nuget&label=nuget)](https://nuget.org/packages/HotAvalonia.Extensions/ "Download HotAvalonia.Extensions from NuGet.org") |

----

## Getting Started

### Installation

To get started, you'll need to add the following three packages to your project:

 - [Avalonia.Markup.Xaml.Loader](https://nuget.org/packages/Avalonia.Markup.Xaml.Loader/) - The official Avalonia package responsible for runtime XAML parsing.
 - [HotAvalonia](https://nuget.org/packages/HotAvalonia/) - The package that integrates hot reload functionality into your application.
 - [HotAvalonia.Extensions](https://nuget.org/packages/HotAvalonia.Extensions/) - The package that provides `.EnableHotReload()` and `.DisableHotReload()` extension methods for greater convenience.

While you could use the `dotnet add` command to accomplish this, I would strongly recommend a more manual yet flexible approach - insert the following snippet into your `.csproj`, `.fsproj`, or `.vbproj` file:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <!-- If you're a .vbproj user, replace ';' with ',' -->
  <DefineConstants>$(DefineConstants);ENABLE_XAML_HOT_RELOAD</DefineConstants>
</PropertyGroup>

<ItemGroup>
  <PackageReference Condition="$(DefineConstants.Contains(ENABLE_XAML_HOT_RELOAD))" Include="Avalonia.Markup.Xaml.Loader" Version="$(AvaloniaVersion)" />
  <PackageReference Condition="$(DefineConstants.Contains(ENABLE_XAML_HOT_RELOAD))" Include="HotAvalonia" Version="2.0.1" />
  <PackageReference Include="HotAvalonia.Extensions" Version="2.0.1" PrivateAssets="All" />
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

### [AvaloniaHotReload]

If you want to be able to refresh a control's state during a hot reload, you can apply the `[AvaloniaHotReload]` attribute to one or more parameterless instance methods of the control. Here's an example:

```diff
  using Avalonia.Controls;
+ using HotAvalonia;

  public partial class FooControl : UserControl
  {
      public FooControl()
      {
          InitializeComponent();
          Initialize();
      }

+     [AvaloniaHotReload]
      private void Initialize()
      {
          // Code to initialize or refresh
          // the control during hot reload.
      }
  }
```

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

 1. **Code Files:** HotAvalonia cannot process `.cs`, `.fs`, `.vb`, or any other code files on its own. Therefore, when creating a new control, typically defined by a pair of `.axaml` and `.axaml.cs` files, you will need to recompile the project to see the changes take effect. However, existing XAML files can be edited freely without requiring recompilation.

 2. **Mobile Development:** Unlike in a local development environment, where your application and project files share the same filesystem, in an emulator, your application is running on what effectively is a remote system. Since HotAvalonia requires direct access to your project files, this scenario is currently unsupported.

 3. **ARM Support:** With the increasing popularity of ARM-based laptops, some of you may already work on such devices. Unfortunately, the tooling required for HotAvalonia to function at its fullest is not yet there. As a result, certain features - such as hot reloading of icons, images, styles, resource dictionaries, and other assets - may not work on ARM machines.

----

## License

Licensed under the terms of the [MIT License](https://github.com/Kir-Antipov/HotAvalonia/blob/HEAD/LICENSE.md).
