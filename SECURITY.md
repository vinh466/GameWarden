# Security policy

GameWarden's Core runs as LocalSystem, accepts network input from the VPN and updates itself from GitHub Releases, so security problems matter even for a small tool.

## Reporting a vulnerability

Report it privately through GitHub: **Security → Report a vulnerability** on this repository. Please do not open a public issue for security problems.

Useful details: the version, what an attacker needs (VPN access, a panel account, a mod, local access), and the steps to reproduce.

## Supported versions

GameWarden is pre-1.0; only the latest release receives fixes.

## Design notes

The threat model and the controls are in [docs/06-security-and-access.md](docs/06-security-and-access.md); process identities and the data-root ACL are in [ADR-0008](decisions/0008-run-as-user-and-launch-chain.md) and [ADR-0009](decisions/0009-data-root-acl-and-secrets.md); the signed update path is in [ADR-0013](decisions/0013-own-setup-and-update-engine.md).
