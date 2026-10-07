# Windows v0.4.1 feature parity

v0.2.0 adds the compact borderless overlay, multi-monitor placement persistence,
pin/opacity controls, keyboard movement, and tooltips/detail view. Provider support
is as listed below.

| Feature | Windows v0.4.1 |
| --- | --- |
| Codex OAuth-file quotas, weekly/session, resets, plan | Implemented |
| Codex saved accounts and auth.json activation | Implemented; Windows DPAPI vault, isolated CLI sign-in, closed-client requirement; real Desktop switch unverified |
| Codex additional model quotas and credit balance | Implemented when supplied |
| Claude OAuth-file session/weekly/model scopes | Implemented when supplied |
| Claude Desktop Code live quota, including Store | Implemented; encrypted Code session, profile identity check, live percentages/reset timestamps; real Store installation verified |
| Claude Desktop selected organization | Checked when its organization cookie is readable; locked-cookie CODE* source qualifies the verified Code organization |
| Claude Desktop recorded quota history | Never used as current quota; legacy historical parser retained |
| Tray, dashboard, close-to-tray, single instance | Implemented |
| Provider/path settings, refresh, low-quota notification | Implemented |
| Login startup, user opt-in | Implemented |
| Stale timestamp, HTTP 401 invalidation, 429 backoff | Implemented |
| Demo without account access | Implemented |
| Other upstream providers | Planned |
| Browser-session imports and device flow | Planned |
| Credential refresh | Owned by provider CLI/Desktop |
| Monthly spend controls, Claude extra spend/routines | Planned |
| Local cost scans, history, charts, service incidents | Planned |
| ARM64, signed installer, auto-update | Planned |

The Windows frontend is independent C# code, preserving the upstream MIT notice.
Upstream Swift sources were not changed. See [the contributing guide](../CONTRIBUTING.md)
for development and validation instructions.
