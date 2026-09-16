<div align="center">

<img src="./art/StoreLogo.svg" alt="Adaptive Card Workbench logo" width="200" height="200">

<h1 align="center">Adaptive Card Workbench</h1>

</div>

Build, template, and preview [Adaptive Cards](https://adaptivecards.io/) with the native WinUI 3 renderer. Adaptive Card Workbench keeps the card payload, sample data, rendered preview, and diagnostics together in a focused Windows desktop workspace.

## Features

- Edit Adaptive Card JSON and sample data side by side.
- Expand Adaptive Card templates and preview them with the native WinUI 3 renderer.
- Organize cards into searchable projects with sorting, duplication, moving, and archiving.
- Autosave the local workspace and restore archived cards or projects.
- Inspect renderer output and validation problems while you work.
- Switch between light, dark, and system themes.

## Installation

### Release package

Download the latest package from the repository's [Releases](../../releases/latest) page and run the installer.

### Build from source

Prerequisites:

- Windows 10 version 1809 (build 17763) or later
- Visual Studio with the Windows application development workload
- .NET 10 SDK

Clone the repository, open `AdaptiveCardWorkbench.slnx` in Visual Studio, select an architecture such as `x64`, and build or deploy the app.

The project can also be restored and built from a Developer PowerShell:

```pwsh
dotnet restore .\AdaptiveCardWorkbench.slnx
dotnet build .\AdaptiveCardWorkbench.slnx --configuration Release
```

Release publishes use .NET Native AOT. Publish an architecture-specific build with:

```pwsh
dotnet publish .\src\AdaptiveCardWorkbench\AdaptiveCardWorkbench.csproj --configuration Release --runtime win-x64 -p:Platform=x64
```

## License

Adaptive Card Workbench is licensed under the [Apache License 2.0](LICENSE.txt).

Third-party software acknowledgements and license information are available in [NOTICE.md](NOTICE.md).

## Security

See [SECURITY.md](SECURITY.md) for the supported-version policy and private vulnerability reporting instructions.

## Author

[Jiří Polášek](https://jiripolasek.com)
