# Publishing this prepared source

1. Create a new GitHub repository named `CodexBar-Windows` (or a name you prefer),
   choose Public, and leave the initial README/license/gitignore options unchecked.
2. Upload the contents of this source folder at the repository root. Include hidden
   `.github` and `.gitignore` files. Do not upload `bin`, `obj`, `dist`, SDK downloads,
   or any login/account/settings files.
3. If using Git locally, initialize this folder, commit the source, add your repository
   remote, and push the `main` branch. The Windows workflow will build and test it.
4. Create a release tagged `v0.4.1`. Attach the separately prepared portable
   `CodexBar-Windows-v0.4.1-win-x64.zip` and its `.sha256` file as release assets.
   The executable ZIP belongs in Releases, not in source control.
   Use **CodexBar for Windows v0.4.1** as the title and copy the text from
   [release notes](docs/RELEASE-v0.4.1.md) into the release description.
5. Enable private vulnerability reporting in the repository security settings.

Suggested description:

> Compact Windows overlay for Codex and Claude quota, with encrypted Codex account switching.

Suggested topics: `windows`, `codex`, `claude`, `quota`, `winforms`, `dotnet`.

The README screenshots are synthetic. Preserve the MIT license and upstream attribution.
The release is unsigned; retain the documented file-storage/closed-client account-switching
limitations and the note that real second-account Desktop behavior remains unverified.

This prepared source was built locally with zero warnings/errors and 105 synthetic checks
passed. GitHub Actions has not run until this source is published to a repository.
Live Claude Desktop Code API quota also matched the user's Usage page; the CODE*
organization-selection qualification is documented in docs/CLAUDE-DESKTOP.md.

The prepared source ZIP includes the hidden GitHub workflow, `.gitignore`,
`.gitattributes`, license, source, tests, icons, screenshots and documentation.
Extract it and upload the **contents** of `CodexBar-Windows` at the repository root.
Do not upload the ZIP itself as the repository's only file.
