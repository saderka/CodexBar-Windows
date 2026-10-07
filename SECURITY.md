# Security

Do not report vulnerabilities with tokens or credentials in public issues.
After the repository is published, use GitHub's **Report a vulnerability** feature
if the maintainer has enabled private vulnerability reporting. Otherwise contact the
repository owner privately through a contact method listed on their GitHub profile.

Include the affected version, impact and a reproduction using synthetic data.
Never attach `auth.json`, `.credentials.json`, the account vault, or login logs.
Claude Desktop's `config.json`, `Local State` and cookie databases also contain login
material and must not be attached.

This project is an unofficial development build, without an audit or signed installer.
Saved accounts use Windows CurrentUser DPAPI; a process running as that same Windows
user can decrypt them. Account switching requires closing clients and does not promise
an atomic compare-and-swap against arbitrary external credential writers.
Desktop quota refresh decrypts the selected profile's stored Code session in memory
and sends authenticated GETs only to fixed Anthropic HTTPS endpoints with redirects
disabled. It does not refresh, log or persist Desktop credentials. When the organization
cookie is locked, CODE* identifies the verified Code organization; it does not assert
the organization currently selected in Desktop. Ambiguous contexts fail closed.
