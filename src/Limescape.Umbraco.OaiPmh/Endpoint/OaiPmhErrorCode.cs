namespace Limescape.Umbraco.OaiPmh.Endpoint;

/// <summary>Error conditions defined by OAI-PMH 2.0, section 3.6.</summary>
public enum OaiPmhErrorCode
{
    BadArgument,
    BadResumptionToken,
    BadVerb,
    CannotDisseminateFormat,
    IdDoesNotExist,
    NoRecordsMatch,
    NoMetadataFormats,
    NoSetHierarchy,
}

internal static class OaiPmhErrorCodeExtensions
{
    /// <summary>The value of the <c>code</c> attribute, for example <c>badVerb</c>.</summary>
    public static string ToProtocolValue(this OaiPmhErrorCode code) => code switch
    {
        OaiPmhErrorCode.BadArgument => "badArgument",
        OaiPmhErrorCode.BadResumptionToken => "badResumptionToken",
        OaiPmhErrorCode.BadVerb => "badVerb",
        OaiPmhErrorCode.CannotDisseminateFormat => "cannotDisseminateFormat",
        OaiPmhErrorCode.IdDoesNotExist => "idDoesNotExist",
        OaiPmhErrorCode.NoRecordsMatch => "noRecordsMatch",
        OaiPmhErrorCode.NoMetadataFormats => "noMetadataFormats",
        OaiPmhErrorCode.NoSetHierarchy => "noSetHierarchy",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
