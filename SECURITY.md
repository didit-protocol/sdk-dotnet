# Security policy

## Reporting a vulnerability

Do not report security vulnerabilities in public issues, discussions or pull requests.

Report them privately, either way:

- GitHub private vulnerability reporting: [open a private report](https://github.com/didit-protocol/sdk-dotnet/security/advisories/new) (Security tab, "Report a vulnerability").
- Email: support@didit.me, with "Security" in the subject.

Include the affected package and version, a description of the issue and its impact, and steps or code to reproduce it.
Never include real API keys, webhook secrets, tokens or customer data; use sandbox values or redact them.

We confirm receipt, keep you informed while we investigate and fix the issue, and credit you in the advisory if you wish.
Please give us reasonable time to release a fix before disclosing the issue publicly.

## Supported versions

Security fixes are released for the latest minor version of the `Didit` and `Didit.Extensions.DependencyInjection` packages.
Upgrade to the latest release to receive them.

| Version | Supported |
|---|---|
| Latest minor release | Yes |
| Older releases | No |

## Scope

This policy covers the code in this repository: the SDK packages, the code generator and the examples.
Vulnerabilities in the Didit API or platform can be reported through the same channels.
