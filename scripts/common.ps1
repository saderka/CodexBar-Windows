$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$WindowsRoot = Split-Path $PSScriptRoot -Parent
$WorkspaceRoot = Split-Path $WindowsRoot -Parent
$LocalSdk = Join-Path $WindowsRoot 'tools/dotnet/dotnet.exe'
$Dotnet = if (Test-Path -LiteralPath $LocalSdk) { $LocalSdk } else { 'dotnet' }
function Invoke-Dotnet {
    & $Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
