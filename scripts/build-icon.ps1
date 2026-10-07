. "$PSScriptRoot/common.ps1"
Invoke-Dotnet run --project "$WindowsRoot/tools/IconBuilder" -- "$WindowsRoot/src/CodexBar.Windows/assets"
