# CodexBar for Windows v0.4.1

Unofficial native Windows port inspired by [steipete/CodexBar](https://github.com/steipete/CodexBar).
Windows 10/11 x64. The portable release includes .NET; no SDK/runtime installation is needed.

## Run

Extract the **entire ZIP** into a permanent folder, then double-click `CodexBar.exe`.
Do not move the EXE away from its supporting files. The default interface is a compact
horizontal overlay (400 × 64 logical pixels for two providers). Provider names, Session and
Weekly are arranged across one row, with blue Codex and orange Claude quota meters.
Percentages mean **remaining** quota. Windows display scaling applies. The executable and
tray share the embedded blue/orange quota-bar icon (16–256 pixel sizes).
Each meter shows its reset countdown directly below the bar. `Reset unknown` means
the API did not supply a reset timestamp; a reset is never guessed.

## Overlay controls

- Drag the **CodexBar header** to place it anywhere on any monitor. Position is remembered.
- Header icons: refresh, pin above other windows, options, hide to tray.
- Right-click or use **⋮** for Settings, usage details, 75–100% opacity, reset position or Quit.
- Click a provider card for full usage details, sources, timestamps, credits and errors.
  Hover for a quick view. Only the first two quota windows appear on the compact card.
- Pin is on by default and can be toggled. Pin/opacity preferences are remembered.
- Escape hides the overlay. Double-click the tray icon to show it again. Ctrl+R refreshes;
  Ctrl+arrow keys move it in 10-pixel steps. Shift+F10 opens options.
- A disconnected monitor or changed display layout moves the overlay into a visible
  working area. It starts near the bottom-right corner, clear of the taskbar.

Use **Quit** from the options/tray menu to exit completely. Upgrading preserves existing
provider/path/source settings. Demo and smoke runs never save overlay preferences.

## Codex accounts

Click the **Codex name** on the overlay, or open **⋮ → Codex accounts**.

1. Open **Manage / add accounts → Save current** to keep your existing login.
2. Click **Sign in another** and choose another ChatGPT account in the browser.
   This uses the official installed Codex CLI in an isolated temporary home and leaves
   your current login unchanged. If the CLI is not found, select its `codex.exe`.
3. Close Codex Desktop, Codex CLI and VS Code. Select a saved account from the overlay
   menu (or select it in the dialog and click **Switch account**).
4. Reopen Codex. CodexBar clears old quota data and fetches for the selected account.

Account switching updates the **auth.json configured in Settings**, and keeps the
outgoing account's latest login so you can switch back. The header shows its saved
name or email. Use Rename for labels such as Personal or Work. Remove only removes
a saved copy; it does not sign out your active Codex account.

Supported: file-based Codex login (`cli_auth_credentials_store = "file"`, the documented
default). Explicit keyring/auto/ephemeral or ambiguous auth config is refused without
changing your config. A custom Codex home affects tools using that same home only.
Already-open clients retain their in-memory login; the app refuses known running
Codex/VS Code clients and never terminates their work. Desktop clients using a separate
credential store are not covered. Desktop switching has not been verified with a real
second account. Reauthenticate a saved account using Sign in another if its session
needs renewal; CodexBar itself does not refresh OAuth tokens.

Sign in using your installed provider CLI first:

- Codex: `codex login`; reads `%USERPROFILE%\.codex\auth.json` (or `CODEX_HOME`).
- Claude Code: run `claude`; reads `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR`).

Settings lets you select full credential-file paths, enable providers, choose 1–60 minute
refresh, configure low-quota notifications, and opt into starting minimized at Windows login.
Startup is off by default. If you move the app after enabling startup, turn it off/on again.
WSL-only or secure-store-only sessions are not automatically discovered. A Claude file token
must have `user:profile` scope. Expired sessions must be renewed by the provider CLI.

**Claude Desktop live quota:** open Desktop's **Code** tab once, then select
**Settings → Claude source → Claude Desktop (live quota)**. No separate CLI login is
needed. CodexBar reads the encrypted Code session in memory, checks its account and
organization against Anthropic's profile API, and requests quota and reset timestamps
from the live usage API. Windows conventional and Microsoft Store profiles are supported.

Automatic uses Desktop live quota only when the default CLI file is missing or contains
no subscription OAuth login. Expired/malformed CLI credentials, API failures and custom
CLI paths never silently select a different source. Existing recorded-Desktop settings
upgrade to live mode; an explicitly disabled Automatic fallback stays disabled.

When Desktop locks its cookie database, **CODE*** means live quota for the sole Code
session bound to the signed-in account. Its organization is verified by the API, but
Desktop's currently selected organization cannot be checked. Hover/click shows that
qualification and the verified organization. Several possible Code organizations are
refused. If the organization cookie is readable, its selection must match the session.
No history-file percentages are used as current quota. See [Desktop details](CLAUDE-DESKTOP.md).

If the session expires, open Desktop's Code tab to let Desktop renew it, then refresh
CodexBar. If several installed profiles are signed in, choose the active Desktop profile
folder in Settings. The app never refreshes or changes Desktop's credentials itself.

For a safe preview without accessing any account:

```powershell
.\CodexBar.exe --demo
```

## Features

Codex/Claude quota windows, remaining percentage, reset countdown, Codex plan/credit balance,
model-specific windows supplied by the service, manual/automatic refresh, tray tooltip,
settings and low-quota notifications. Transient errors mark last good data **STALE**.
Changing a credential source/account or receiving HTTP 401 clears previous account data.
Provider 429 retry times are respected even for manual refresh. A reset countdown reaching
zero requires a new reading; it never invents a quota reset.

This release does **not** implement all upstream providers, local cost scans, browser-session imports,
automatic token refresh, billing graphs, update service, or a signed installer.
Provider APIs may change. Check [feature parity](FEATURE-PARITY.md) and the
[contributing guide](../CONTRIBUTING.md).

## Privacy

Normal quota refresh reads credentials and sends them over verified HTTPS to fixed
provider hosts; redirects are disabled. Desktop mode decrypts only the selected Claude
profile's stored OAuth cache with Windows CurrentUser DPAPI/AES-GCM, and reads only its
`lastActiveOrg` cookie when accessible. It never extracts web session cookies, reads other
browsers or process memory, copies cookie databases, or writes Desktop login files.
Account actions explicitly save complete Codex
login files encrypted with Windows CurrentUser DPAPI in
`%LOCALAPPDATA%\CodexBarWindows\accounts`, with a user-only/System directory ACL.
Encrypted recovery of the previous login is saved before activation. Saved accounts
are tied to your Windows user; they are not included in portable release ZIPs.
Sign in another invokes the official CLI only when clicked, drains its output without
logging it, and removes its isolated temporary home after exit/cancellation.
No tokens, login URLs, raw API responses or usage history are logged. Non-secret
preferences are stored in `settings.json` beside the account directory. No telemetry.
The Windows login toggle only controls this app's own HKCU Run entry. No admin needed.

## Build and checks

Requires .NET 10 SDK. Scripts use the workspace `tools/dotnet` SDK when available,
otherwise the SDK on PATH. No third-party application packages.

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1
```

Tests use synthetic credentials, a fake child login process and fake HTTP only.
The console suite also checks synthetic encrypted Desktop credentials, native SQLite,
exclusive file locks, account verification, source qualification and HTTP backoff.
`CodexBar.exe --smoke-test` checks Windows DPAPI and synthetic account switching,
renders the overlay/account dialog and saves results under `smoke` next to the executable.
Live browser sign-in, second-account Desktop acceptance and a human tray click are separate
from these automated checks. This unsigned development build may receive a Windows
SmartScreen warning; a signed distribution is future work.

Upstream reference: `5c7735f8afa8d7ab2480766e1316ad16bb7aeb67`.

Live Desktop Code quota was verified on a real Microsoft Store installation and matched
the user's Usage page for both quota windows and reset times. Provider storage and
undocumented API formats can change; see [verification](VERIFICATION.md) for scope.
