# CodexBar for Windows v0.4.1

A compact, movable overlay for Codex and Claude coding quota on Windows 10/11 x64.

## Changes

- Fix `100%` being cut off in Session and Weekly meters for both providers.
- Keep the same compact horizontal layout, font sizes, blue Codex and orange Claude.
- Include the live Claude Desktop Code quota and reset-time support introduced in v0.4.0.
- Keep saved Codex accounts, encrypted Windows storage, tray controls and placement settings.

## Install or upgrade

Download **CodexBar-Windows-v0.4.1-win-x64.zip**, extract the entire folder, and run
`CodexBar.exe`. Quit an older instance before opening this version. The ZIP includes
the required .NET runtime. Settings and saved Codex accounts stay in your Windows user
profile; they are not stored in the portable folder.

For the Claude Desktop Code tab, choose **Settings → Claude source → Claude Desktop
(live quota)**. Open Desktop's Code tab first to establish or renew its session.
The overlay shows **remaining** quota.

## Verification and limitations

- Source build succeeded without warnings or errors; 105 synthetic checks passed.
- Packaged UI and missing-credentials smoke checks passed. The 100% overlay was visually inspected.
- Authorized real Claude Desktop Code API percentages and reset times matched its Usage view.
- **CODE\*** identifies a verified Code credential when Desktop's locked cookie database
  prevents checking its currently selected organization. Hover/click for the organization
  and qualification; ambiguous Code organizations are refused.
- Codex account switching changes the configured file-based login. Close Codex clients
  and VS Code before switching, then reopen them. Real second-account Codex Desktop
  acceptance is not verified.
- The build is unsigned. Windows may show a SmartScreen prompt. Provider API/storage
  formats can change; see the source documentation for supported behavior.

## Release assets

- `CodexBar-Windows-v0.4.1-win-x64.zip` — portable Windows app.
- `CodexBar-Windows-v0.4.1-win-x64.zip.sha256` — SHA256 checksum.

The source is in the repository. The prepared source ZIP is for importing the project;
it does not contain the compiled application.
