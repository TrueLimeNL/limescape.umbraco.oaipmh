# Contributing

Thanks for helping improve the OAI-PMH provider.

## Ground rules

- The package is generic: no customer names in code, namespaces, tables, labels or defaults. Site-specific behaviour belongs in configuration.
- The endpoint is read-only and public. Never expose unpublished content, confidential elements or fields flagged "not for export".
- Every response must validate against the official OAI-PMH 2.0 schema. Add or extend a test that validates the XML with `XsdValidator` for each change in output.
- Protocol behaviour follows the [OAI-PMH 2.0 specification](https://www.openarchives.org/OAI/openarchivesprotocol.html); reference the section in the pull request when you change it.

## Workflow

1. Open an issue describing the problem or the protocol gap.
2. Create a branch, make the change with tests, run `dotnet build` and `dotnet test` (warnings are errors).
3. Open a pull request. CI builds, tests and packs the package.

Releases are published to NuGet by pushing a tag `oaipmh-v<version>`, for example `oaipmh-v1.0.0`.

## Licence

By contributing you agree that your contribution is licensed to TrueLime B.V. as set out in article 9 of the LOPL (see LICENSE).
