using System.Globalization;
using System.Text;
using System.Xml;
using Limescape.Umbraco.OaiPmh.Schemas;

namespace Limescape.Umbraco.OaiPmh.Endpoint;

/// <summary>Writes an OAI-PMH response that carries a single error.</summary>
internal static class ErrorWriter
{
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    internal static readonly XmlWriterSettings WriterSettings = new()
    {
        Async = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
    };

    /// <summary>
    /// Writes the response document. For <c>badVerb</c> and <c>badArgument</c> the <c>request</c> element carries
    /// no attributes (OAI-PMH 2.0, section 3.2). The endpoint adds validated request attributes for other errors.
    /// </summary>
    public static async Task WriteAsync(Stream output, string requestUrl, OaiPmhErrorCode code, string message, DateTimeOffset responseDate)
    {
        await using var xml = XmlWriter.Create(output, WriterSettings);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "OAI-PMH", OaiPmhSchemas.OaiPmhNamespace);
        await xml.WriteAttributeStringAsync("xmlns", "xsi", null, XsiNamespace);
        await xml.WriteAttributeStringAsync("xsi", "schemaLocation", XsiNamespace,
            $"{OaiPmhSchemas.OaiPmhNamespace} {OaiPmhSchemas.OaiPmhSchemaLocation}");

        await xml.WriteElementStringAsync(null, "responseDate", OaiPmhSchemas.OaiPmhNamespace, FormatResponseDate(responseDate));
        await xml.WriteElementStringAsync(null, "request", OaiPmhSchemas.OaiPmhNamespace, requestUrl);

        await xml.WriteStartElementAsync(null, "error", OaiPmhSchemas.OaiPmhNamespace);
        await xml.WriteAttributeStringAsync(null, "code", null, code.ToProtocolValue());
        await xml.WriteStringAsync(message);
        await xml.WriteEndElementAsync();

        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
        await xml.FlushAsync();
    }

    /// <summary><c>responseDate</c> is always UTC with second granularity, regardless of the repository granularity.</summary>
    internal static string FormatResponseDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
