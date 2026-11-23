# Changelog

All notable changes to this package are recorded here. The format follows Keep a Changelog and the versions follow Semantic Versioning.

## 1.0.0 - 2026-11-23

First public version of the OAI-PMH 2.0 data provider for Umbraco 17 LTS:

- The six OAI-PMH verbs over GET and POST, with Dublin Core (`oai_dc`).
- Persistent deleted records (tombstones) and signed resumption tokens.
- Confidentiality rules applied in the selector and again in the writer.
- Responses validated offline against the embedded official schemas.
