# Limescape.Umbraco.OaiPmh

Generic OAI-PMH 2.0 provider for Umbraco 17, available under the LOPL 1.0. The six verbs accept GET and form-urlencoded POST.
Dublin Core, inclusive UTC date filtering, persistent tombstones with a nightly consistency check, confidential exclusion, signed keyset pagination and per-IP rate limiting are implemented.
ListSets returns noSetHierarchy. NL-LOM, IEEE-LOM and sets are not part of version 1.0.0.

## Configure

The package composer registers the endpoint and content notification handlers; its package migration creates lsOaiRecord.
All site-specific behavior is configuration under Limescape:OaiPmh, with no reference to a host assembly:

```json
{
  "Limescape": {
    "OaiPmh": {
      "EndpointPath": "/oai",
      "RepositoryName": "Learning materials",
      "RepositoryIdentifier": "materials.example.org",
      "AdminEmail": "repository-admin@example.org",
      "BaseUrl": "https://materials.example.org/oai",
      "PageSize": 100,
      "Granularity": "Seconds",
      "DocumentTypes": [ "learningMaterial" ],
      "SharingLevelAlias": "sharingLevel",
      "PublicSharingValue": "public",
      "ConfidentialAlias": "confidential",
      "ArchivedAlias": "archivedAt",
      "NonExportableFields": [ "internalNotes" ],
      "ConsistencyJobTime": "02:00:00",
      "LandingPageUrlTemplate": "https://materials.example.org/materials/{key}",
      "DublinCoreMapping": {
        "title": [ "title" ],
        "description": [ "description" ],
        "creator": [ "creator" ],
        "rights": [ "license" ],
        "subject": [ "subject", "keywords" ]
      }
    }
  }
}
```

Supply a base64 TokenSigningKey of at least 32 random bytes through the site's secret store.
Use the same stable key on all replicas. Without it the package creates a random process-local key, so restarting invalidates outstanding tokens.
RepositoryIdentifier must remain stable after the first harvest. Existing identifiers are retained even if the setting changes.

| Setting | Default / behavior |
|---|---|
| Enabled | true; tenant-level switch, false returns 404 |
| EndpointPath | /oai |
| RepositoryName / AdminEmail | Learning materials / admin@example.org; replace both with the real repository identity |
| BaseUrl | Derived from the request, including trusted forwarded headers supplied by the host |
| Granularity | Seconds; Day is supported, but never accepts second-granularity filter arguments |
| PageSize | 100; range 1–1000 |
| MaxPageBytes | 9,000,000 UTF-8 bytes, adaptive page size with 4 KiB envelope/token reserve; a single oversized record is indivisible. Responses over 9.9 MB fail with 503 / Retry-After: 60 and no partial XML; reduce the mapped export size before retrying |
| RequestsPerSecond | 10 per remote IP; overload returns HTTP 503 with Retry-After: 1 |
| TokenLifetime | 24 hours maximum; shorter lifetimes may be configured |
| DocumentTypes | lsLearningMaterialBase, lsMaterialGroup; matches document aliases and compositions |
| SharingLevelAlias / PublicSharingValue | sharingLevel / public; a missing value fails closed. An empty alias opts out for sites without a sharing property |
| ConfidentialAlias | confidential; true or 1 excludes the document. The property itself is never exported, even if mapped |
| ArchivedAlias | archivedAt; any published value (e.g. an archive date) takes the document out of public scope (tombstone). Empty opts out |
| NonExportableFields | Empty; explicit deny-list (case-insensitive) applied in the selector and again in the writer |
| ConsistencyJobEnabled | true; nightly reconciliation of lsOaiRecord with the content, see below |
| ConsistencyJobTime | 02:00:00 UTC; time of day of the reconciliation, runs on the scheduling server only |
| Culture | Null (invariant properties). Configured cultures must be published; there is no edited-value fallback |
| LandingPageUrlTemplate | /materials/{key}; absolute HTTP(S) URI or root-relative route. Configure the actual site's landing route |
| DublinCoreMapping | title, description, creator, date, type, format, identifier, language, rights (license), subject, source |

Each mapping contains property aliases, not arbitrary object paths. Only primitive published values from the built-in text, label, numeric, boolean and date editors are exported. Custom property editors require a reviewed selector implementation.
Media pickers, block values, structured arrays and JSON objects are deliberately excluded rather than recursively serialized.
The writer adds an absolute landing-page dc:identifier and rechecks the mapping allow-list and non-exportable fields.
The built-in flexible dropdown editor safely emits repeated DC values from an array of strings. For other multivalue metadata, map multiple primitive properties to the same DC element; complex editor conversion belongs in a separate selector implementation.
Configured DocumentTypes and DublinCoreMapping replace their defaults completely. Omitted settings use the defaults.

## Tombstones (deletedRecord persistent)

Identify advertises `deletedRecord=persistent`. A document gets a row in lsOaiRecord the first time it is public; from then on its identifier is never removed or reused.
Publishing hashes the exported values and landing route; an internal save or an unchanged publish never advances the datestamp.

A row becomes a tombstone (`status="deleted"`, a new datestamp, no metadata) when the document stops being public:

- unpublish, move to the recycle bin and delete, including all descendants when this happens to an ancestor (Umbraco notifies only the subtree root);
- a move below an unpublished ancestor;
- a publish that makes it non-public: sharing level other than public, confidential, archived, or a culture that is no longer published;
- purge (emptying the recycle bin): the tombstone and its datestamp stay unchanged. Rows have no foreign key to content and this package never deletes them.

Republishing (or republishing an ancestor) restores the original identifier with a new datestamp. A restore from the recycle bin does not republish by itself.
ListIdentifiers, ListRecords and GetRecord show the deleted header. A tombstone keeps answering ListMetadataFormats with the formats it had.
The endpoint also rechecks current public visibility for every active row: content that turned private before its row was updated is served as deleted, never with metadata.

### Consistency job

ConsistencyJob runs daily at ConsistencyJobTime (UTC) on the scheduling server. It compares every document of the configured types and every active row with the actual content and:

- creates rows for public content without one (first install on an existing repository; no republish needed after the first night);
- refreshes rows whose export changed without a notification (also after a mapping change, which is a real metadata change for harvesters);
- restores rows of content that is public again, and tombstones rows of content that is no longer public or no longer exists.

Each correction advances the datestamp, is logged as a warning with the document key and its kind, and the pass ends with an information summary. A second pass without changes corrects nothing.

## Confidentiality

Nothing reaches the output unless it passes every rule below, first in the ContentSelector and again in OaiDcWriter (defence in depth, also against a custom IContentSelector):

- only document types in DocumentTypes, published, public sharing level, not confidential, not archived;
- only aliases listed in DublinCoreMapping, minus NonExportableFields and the ConfidentialAlias property;
- only primitive values of the built-in text, label, numeric, boolean, date and flexible dropdown editors. Media pickers, block values, JSON and Umbraco references (`umb://...`) are dropped.

Elements (files) of a material are therefore never exported in version 1.0.0: no name, URL, size or key of any element, confidential or not, appears in any verb, format or HTTP method.
A confidential element is a media item with a true `confidential` property; it needs no OAI-specific setting. A document-level flag (ConfidentialAlias) hides the whole record.
Future formats (NL-LOM, IEEE-LOM) must go through the same selector and writer rules; the leak tests harvest every format that ListMetadataFormats advertises.

## Tokens

Pagination orders by (datestamp, database id), which works when every row has the same second.
Tokens are HMAC-SHA256 signed, bound to verb, prefix, bounds, configuration and a revision of the repository.
A changed export (including a change within the same second) invalidates the token with badResumptionToken.
The last page of a resumed list carries an empty resumptionToken. Replaying a valid token against unchanged records returns the same page.
GET and POST (form-urlencoded, optional charset) return identical responses. POST arguments belong solely in the form body; duplicate, empty, unknown and mixed query/body arguments and other body types are rejected with badArgument.

Do not advertise Edurep registration before the release conformance checks are complete.

## Tests and schemas

Run `dotnet test --project tests/Limescape.Umbraco.OaiPmh.Tests` from the repository root.
The suite validates responses offline against the embedded official OAI-PMH, oai_dc, DCMI and W3C schemas, including a 50,000-record harvest.
The tests of the Limescape content layer host, which are not in this repository, exercise the actual SQLite migration, publish notifications, concept isolation, descendant tombstones, purge persistence, the consistency job and a hostile-mapping leak harvest over GET and POST.
Standalone endpoint users register IRecordStateStore and IContentSelector alongside AddOaiPmh and MapOaiPmh.

The schema files are included unmodified; OaiPmhSchemas.CreateSchemaSet() returns a compiled offline schema set.

## Licence and publication

Copyright © 2026 TrueLime B.V. Source available under the Limescape Open Platform Licentie (LOPL), versie 1.0, see [LICENSE](LICENSE). The licence text is in Dutch and the Dutch version prevails. The LOPL is a source-available licence, not an OSI-approved open-source licence.

TrueLime B.V. grants the rights under the LOPL to public organisations and other non-profit organisations that register as a user at https://www.limescape.ai and accept the licence. Any other use, such as commercial hosting, managed services, OEM or white-label use, requires a commercial licence from TrueLime B.V. Changes per version are in [CHANGELOG.md](CHANGELOG.md).