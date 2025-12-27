# .NET 8.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 8.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 8.0 upgrade.
3. Convert AI-CAD-December.csproj to SDK-style project.
4. Upgrade AI-CAD-December.csproj to .NET 8.0.

## Settings

This section contains settings and data used by execution steps.

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                             | Current Version | New Version | Description                                                                            |
|:-----------------------------------------|:---------------:|:-----------:|:---------------------------------------------------------------------------------------|
| Newtonsoft.Json                          | 13.0.3          | 13.0.4      | Recommended for .NET 8.0                                                               |
| System.Runtime.CompilerServices.Unsafe   | 5.0.0           | 6.1.2       | Package is deprecated and needs upgrade (see https://github.com/dotnet/announcements/issues/217) |

### Project upgrade details

This section contains details about each project upgrade and modifications that need to be done in the project.

#### AI-CAD-December.csproj modifications

Project properties changes:
  - Project must be converted to SDK-style format
  - Target framework should be changed from `net472` to `net8.0-windows`

NuGet packages changes:
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended for .NET 8.0*)
  - System.Runtime.CompilerServices.Unsafe should be updated from `5.0.0` to `6.1.2` (*deprecated package upgrade*)

Other changes:
  - Project file will be converted from traditional .NET Framework format to modern SDK-style format which is more concise and supports .NET 8.0
