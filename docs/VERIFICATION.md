# Verification for v0.4.1

UI patch: reproduced the `100%` truncation with a synthetic full-quota overlay, then
reallocated card text width. The unchanged 11.5-pixel percentage font requires about
34.54 logical pixels for `100%`; the two-provider layout now provides 35.56 instead
of 29.04. The overlay remains 400 x 64 logical pixels. The existing UI smoke captures
both providers' Session/Weekly at 100% for visual inspection.

Checked on Windows x64 with .NET SDK 10.0.401. No changes were made to upstream Swift.

- Release build: zero warnings/errors.
- Console suite: 105 checks, synthetic files and fake HTTP only. Includes account/org
  selection, authenticated AES-GCM tamper rejection, DPAPI-encrypted Desktop fixtures,
  native read-only SQLite, Chromium v24 host binding, actual Windows exclusive file
  locks, V2 tombstones, profile identity mismatches, concurrent session changes,
  redirected responses, and account-scoped Retry-After across token rotation.
- Portable package runs demo overlay/account/settings checks and a missing-credentials
  regression before ZIP creation. Synthetic CODE* source qualification and migration
  of old recorded-Desktop settings are included. Smoke never reads real credentials.
- Authorized live read on the official Microsoft Store Claude Desktop 2.19675.1.0
  profile succeeded through /api/oauth/profile and /api/oauth/usage. Both Session and
  Weekly percentages and reset timestamps were independently compared with Desktop's
  Usage view and matched. Personal quota values are omitted from this public report.
- The live run exercised the cookie-lock path. The current account and Code credential
  organization were API-verified; Desktop's selected organization was unavailable.
  Production exposes this distinction with CODE* and source details. The readable
  cookie path and ambiguous/expired/mismatched contexts were verified with synthetic
  native fixtures, not a real second organization.

Real Desktop token renewal, multiple real organizations/accounts, every older/newer
Desktop build, ARM64, signing, installer, and Codex Desktop second-account acceptance
remain outside this verification. Native API/storage formats are undocumented and may
change. No raw credentials, account IDs or response bodies were written to this report.
