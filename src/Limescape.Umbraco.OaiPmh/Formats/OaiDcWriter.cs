using System.Xml.Linq;
using Limescape.Umbraco.OaiPmh.Configuration;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Schemas;

namespace Limescape.Umbraco.OaiPmh.Formats;

/// <summary>Allow-listed Dublin Core output. Exclusions are applied again before serialization.</summary>
public static class OaiDcWriter
{
    internal static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    { "title", "creator", "subject", "description", "publisher", "contributor", "date", "type", "format", "identifier", "source", "language", "relation", "coverage", "rights" };
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace OaiDc = "http://www.openarchives.org/OAI/2.0/oai_dc/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static XElement Write(ExportContent content, OaiPmhOptions options, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(baseUrl);
        var dc = new XElement(OaiDc + "dc", new XAttribute(XNamespace.Xmlns + "oai_dc", OaiDc),
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(Xsi + "schemaLocation", $"{OaiDc} http://www.openarchives.org/OAI/2.0/oai_dc.xsd"));
        foreach (var field in content.Fields)
        {
            // Defence in depth against any IContentSelector: the same allow-list, deny-list and scalar rules as the selector.
            if (Elements.Contains(field.Element) && !ContentSelector.IsExcluded(field.Alias, options)
                && options.DublinCoreMapping.TryGetValue(field.Element, out var aliases)
                && aliases.Contains(field.Alias, StringComparer.OrdinalIgnoreCase)
                && ContentSelector.Scalar(field.Value) is { Length: > 0 } value)
            {
                dc.Add(new XElement(Dc + field.Element, value));
            }
        }
        var landing = new Uri(baseUrl, content.LandingPage);
        if (landing.Scheme != Uri.UriSchemeHttps && landing.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("Landing pages must use HTTP(S).");
        }

        dc.Add(new XElement(Dc + "identifier", landing.AbsoluteUri));
        return new XElement((XNamespace)OaiPmhSchemas.OaiPmhNamespace + "metadata", dc);
    }
}
