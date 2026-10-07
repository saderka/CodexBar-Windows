# Claude Desktop live quota

v0.4.0 reads the live quota API using the encrypted OAuth session created by Desktop's
Code tab. Historical `plan-usage-history.json` samples are not current quota.

1. Sign in to Claude Desktop and open its **Code** tab.
2. In CodexBar, open **Settings** and choose **Claude Desktop (live quota)**.
3. Save and refresh. Hover/click Claude for the source, fetch time, organization,
   quota windows, countdowns and exact local reset times.

The overlay shows **remaining** quota. For example, 65% used in Claude equals 35%
remaining in CodexBar. Resets come from the server; unknown/inactive-window resets
stay unknown. The window stays compact and horizontal.

## Sources and account binding

- **Claude Desktop live API:** the account and organization returned by the profile API
  match the account hint and active organization cookie in the selected Desktop profile.
- **Claude Desktop Code live API / CODE\*:** Windows has exclusively locked the cookie
  database. CodexBar uses the sole matching Code credential for the current account and
  verifies its account/organization with the API. These are fresh API values for that
  Code organization. Desktop's currently selected organization remains unverified;
  the details explicitly say so. Several plausible Code organizations are refused.
- **STALE:** a transient request failed; the last successful data is marked stale.
  Authentication rejection, unknown context or a detected account/session change clears
  previous values. No historical sample replaces a failed live request.

V2 encrypted cache data is authoritative when present. Logout tombstones, ambiguous
credentials, expired sessions, unsupported encryption and account/organization
mismatches fail closed. A session is reread after the API requests; a concurrent change
discards the response. Desktop owns renewal: open Code and refresh after an expiry.

Normal Windows and the official Microsoft Store profile are detected. If both are
signed in, choose the intended profile folder under **Desktop profile folder** in
Settings. A custom CLI credential path never automatically falls back to Desktop.

## Privacy and compatibility

CodexBar reads the selected profile's `config.json`, `Local State`, and only the
`lastActiveOrg` cookie from `Network/Cookies`. It decrypts stored Code credentials in
memory using DPAPI and authenticated AES-GCM. The cookie database is opened read-only;
it is never copied. No web session cookies, unrelated browser profiles or process
memory are read. No credentials or raw API bodies are logged or persisted.

Authenticated GETs go only to these fixed HTTPS endpoints, with redirects disabled:

- `https://api.anthropic.com/api/oauth/profile`
- `https://api.anthropic.com/api/oauth/usage`

Both endpoints and Desktop's storage format are provider implementation details and
can change. Support was verified with Desktop 2.19675.1.0 on Windows x64. This does not
establish compatibility with every past/future Desktop version or third-party client.

The optional source-only `tools/ClaudeDesktopProbe` is an explicit live diagnostic;
ordinary test/smoke commands use synthetic files and fake HTTP only.
