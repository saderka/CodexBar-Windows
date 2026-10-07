# Contributing

Use Windows and the .NET 10 SDK. Run `scripts/test.ps1` and `scripts/build.ps1`
before submitting a pull request. For UI changes, run `scripts/package.ps1` and inspect
the synthetic screenshots in the packaged `smoke` directory.

Keep changes focused and describe the behavior, reproduction steps and validation.
Use only synthetic credentials in tests. Never commit login files, account vaults,
provider responses containing private data, or screenshots with personal information.
Preserve compatibility with existing settings and upstream attribution.

The app uses native WinForms and .NET libraries with no additional NuGet dependencies.
