. "$PSScriptRoot/common.ps1"
$OutputFolder = Join-Path $WindowsRoot 'dist/CodexBar-Windows-v0.4.1-win-x64'
$PackageArguments = @('publish', "$WindowsRoot/src/CodexBar.Windows", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', $OutputFolder)
$LocalFeed = Join-Path $WindowsRoot 'tools/nuget'
if (Test-Path -LiteralPath $LocalFeed) { $PackageArguments += "-p:RestoreSources=$LocalFeed" }
Invoke-Dotnet @PackageArguments
Copy-Item -LiteralPath "$WindowsRoot/LICENSE", "$WindowsRoot/README.md", "$WindowsRoot/THIRD-PARTY-NOTICES.md" -Destination $OutputFolder
New-Item -ItemType Directory -Force "$OutputFolder/docs" | Out-Null
Copy-Item -LiteralPath "$WindowsRoot/docs/FEATURE-PARITY.md", "$WindowsRoot/docs/CLAUDE-DESKTOP.md", "$WindowsRoot/docs/VERIFICATION.md", "$WindowsRoot/docs/USER-GUIDE.md" -Destination "$OutputFolder/docs"
$SmokeProcess = Start-Process -FilePath "$OutputFolder/CodexBar.exe" -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
if (-not $SmokeProcess.WaitForExit(20000)) {
    Stop-Process -Id $SmokeProcess.Id
    throw 'Packaged app smoke check exceeded 20 seconds.'
}
if ($SmokeProcess.ExitCode -ne 0 -or (Get-Content -LiteralPath "$OutputFolder/smoke/result.txt" -Raw) -notmatch '^PASS:') {
    throw 'Packaged app smoke check failed.'
}
$MissingProcess = Start-Process -FilePath "$OutputFolder/CodexBar.exe" -ArgumentList '--missing-credentials-smoke' -WindowStyle Hidden -PassThru
if (-not $MissingProcess.WaitForExit(20000)) {
    Stop-Process -Id $MissingProcess.Id
    throw 'Missing-credentials regression check exceeded 20 seconds.'
}
if ($MissingProcess.ExitCode -ne 0 -or (Get-Content -LiteralPath "$OutputFolder/smoke/missing-credentials-result.txt" -Raw) -notmatch '^PASS:') {
    throw 'Missing-credentials regression check failed.'
}
$Archive = Join-Path $WindowsRoot 'dist/CodexBar-Windows-v0.4.1-win-x64.zip'
Compress-Archive -Path "$OutputFolder/*" -DestinationPath $Archive -Force
$Checksum = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$Archive.sha256" -Value "$Checksum  CodexBar-Windows-v0.4.1-win-x64.zip"
Write-Output "Package: $Archive"
