# Security Policy

## Supported Versions

LinkView is currently a 0.9.x engineering preview. Security fixes are applied to the latest revision on the `main` branch.

## Reporting a Vulnerability

Please report suspected vulnerabilities privately through GitHub Security Advisories for this repository:

1. Open the repository's **Security** tab.
2. Select **Advisories** and create a new private advisory.
3. Include affected version or commit, reproduction steps, impact, and any suggested mitigation.

Do not open a public issue for an unpatched vulnerability. Please avoid including real credentials, screen content, or other sensitive user data in reports.

## Scope and Known Constraints

- The signaling service has no built-in TLS. Deployments must place it behind a TLS reverse proxy and use `wss://`. The sample configuration uses cleartext `ws://` for local development only.
- The Android build permits cleartext traffic for local development. Production builds should disable it.
- The signaling service is a single-instance in-memory service without persistence, audit logging, or centralized rate limiting.
- Pairing codes are intended to authorize session admission, not to replace DTLS-SRTP media encryption.
- The Android client has not yet been validated on physical devices.

See [docs/SECURITY.md](docs/SECURITY.md) for the threat model and implementation details.
