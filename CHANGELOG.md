# Changelog

## 0.4.1 — 2026-10-07

- Display `100%` fully in both Session and Weekly meters for Codex and Claude.
- Reallocate space within each compact card; keep the same overlay size and fonts.
- Include a synthetic full-quota overlay capture in the existing Windows smoke check.

## 0.4.0 — 2026-10-07

- Read live Claude Desktop Code quota and reset times using its encrypted OAuth session.
- Verify the credential's account and organization with Anthropic's profile API.
- Handle Windows' exclusive cookie lock with a qualified CODE* source for a unique
  current-account Code credential. Refuse ambiguous organizations and account changes.
- Replace Automatic historical fallback with live Desktop API; migrate old Desktop
  preferences while preserving disabled fallback. Add optional Desktop profile selection.
- Respect API backoff across token rotation and show exact local reset times in details.
- Add native encrypted Desktop/SQLite/file-lock checks. The 105-check synthetic suite
  passes; an authorized real Store Code session matched Desktop's Usage page.

## 0.3.3 — 2026-10-06

- Hide historical Desktop quota percentages on the overlay/tray because they cannot
  verify current quota or account. Keep historical samples in details only.

## 0.3.2 — 2026-10-06

- Show reset countdowns directly on Session and Weekly meters without increasing overlay size.
- Show Reset unknown when no reset timestamp is supplied, including Claude Desktop cache.

## 0.3.1 — 2026-10-06

- Fix dark account submenu text, including disabled items and submenu arrows.
- Keep account checkmarks visible on a matching dark background.

## 0.3.0 — 2026-10-06

- Add saved Codex accounts, isolated official CLI sign-in and auth-file activation.
- Protect saved login files with Windows CurrentUser DPAPI and restricted directory ACL.
- Preserve outgoing refreshed credentials and invalidate old account quota on selection.

## 0.2.1 — 2026-10-06

- Compact horizontal overlay with Session and Weekly meters side by side.
- Blue Codex/orange Claude colors and embedded multi-resolution executable icon.
