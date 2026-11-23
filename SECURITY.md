# Security policy

## Supported versions

The latest minor release of the current major version receives security fixes.

## Reporting a vulnerability

Do not open a public issue. Report privately through GitHub's "Report a vulnerability" (Security tab of the repository). Include the package version, the request that triggers the problem and the observed response.

We acknowledge reports within five working days and aim to release a fix within 30 days for confirmed issues.

## Scope

Especially relevant for this package:

- Exposure of unpublished content, confidential elements or fields flagged "not for export" through any verb or format.
- Resumption token tampering (forged or replayed tokens).
- Denial of service through expensive requests that bypass rate limiting.
- XML injection in responses.
