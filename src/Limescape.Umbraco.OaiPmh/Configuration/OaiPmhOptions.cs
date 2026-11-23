namespace Limescape.Umbraco.OaiPmh.Configuration;

/// <summary>Generic settings bound from Limescape:OaiPmh. Published invariant properties are exported by default.</summary>
public sealed class OaiPmhOptions
{
    public const string SectionName = "Limescape:OaiPmh";
    public bool Enabled { get; set; } = true;
    public string EndpointPath { get; set; } = "/oai";
    public string RepositoryName { get; set; } = "Learning materials";
    public string RepositoryIdentifier { get; set; } = "repository.local";
    public string AdminEmail { get; set; } = "admin@example.org";
    public Uri? BaseUrl { get; set; }
    public OaiPmhGranularity Granularity { get; set; } = OaiPmhGranularity.Seconds;
    public int PageSize { get; set; } = 100;
    public int MaxPageBytes { get; set; } = 9_000_000;
    public int RequestsPerSecond { get; set; } = 10;
    /// <summary>Stable base64 key of at least 32 bytes. Empty uses a process-local random key (tokens expire on restart).</summary>
    public string TokenSigningKey { get; set; } = string.Empty;
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(24);
    /// <summary>Aliases of document types or compositions. No dependency on any host assembly.</summary>
    public string[] DocumentTypes { get; set; } = ["lsLearningMaterialBase", "lsMaterialGroup"];
    public string SharingLevelAlias { get; set; } = "sharingLevel";
    public string PublicSharingValue { get; set; } = "public";
    public string ConfidentialAlias { get; set; } = "confidential";
    /// <summary>A published non-empty value (e.g. an archive date) takes the document out of public scope: it becomes a tombstone. Empty opts out.</summary>
    public string ArchivedAlias { get; set; } = "archivedAt";
    public string[] NonExportableFields { get; set; } = [];
    /// <summary>Nightly reconciliation of lsOaiRecord with the content (backfill, drift, tombstones). Runs on the scheduling server.</summary>
    public bool ConsistencyJobEnabled { get; set; } = true;
    /// <summary>UTC time of day of the nightly reconciliation.</summary>
    public TimeSpan ConsistencyJobTime { get; set; } = TimeSpan.FromHours(2);
    /// <summary>Optional published culture. No draft fallback; segments and complex editor values are excluded.</summary>
    public string? Culture { get; set; }
    /// <summary>Absolute HTTP(S) URI or root-relative route with {key}. Supply the actual landing-page route per site.</summary>
    public string LandingPageUrlTemplate { get; set; } = "/materials/{key}";
    public Dictionary<string, string[]> DublinCoreMapping { get; set; } = new(StringComparer.Ordinal)
    {
        ["title"] = ["title"],
        ["description"] = ["description"],
        ["creator"] = ["creator"],
        ["date"] = ["date", "originalDate"],
        ["type"] = ["type"],
        ["format"] = ["format"],
        ["identifier"] = ["identifier"],
        ["language"] = ["language"],
        ["rights"] = ["license"],
        ["subject"] = ["subject"],
        ["source"] = ["source", "provenance"]
    };
}

public enum OaiPmhGranularity { Seconds, Day }
