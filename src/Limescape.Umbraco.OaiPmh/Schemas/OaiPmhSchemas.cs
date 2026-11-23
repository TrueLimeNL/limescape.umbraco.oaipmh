using System.Xml;
using System.Xml.Schema;

namespace Limescape.Umbraco.OaiPmh.Schemas;

/// <summary>
/// The official OAI-PMH 2.0 and Dublin Core schemas, embedded in the package so responses can be validated without network access.
/// </summary>
public static class OaiPmhSchemas
{
    /// <summary>OAI-PMH 2.0 namespace.</summary>
    public const string OaiPmhNamespace = "http://www.openarchives.org/OAI/2.0/";

    /// <summary>Location of the OAI-PMH 2.0 schema, used in <c>xsi:schemaLocation</c>.</summary>
    public const string OaiPmhSchemaLocation = "http://www.openarchives.org/OAI/2.0/OAI-PMH.xsd";

    /// <summary><c>oai_dc</c> container namespace.</summary>
    public const string OaiDcNamespace = "http://www.openarchives.org/OAI/2.0/oai_dc/";

    /// <summary>Location of the <c>oai_dc</c> schema.</summary>
    public const string OaiDcSchemaLocation = "http://www.openarchives.org/OAI/2.0/oai_dc.xsd";

    /// <summary>Dublin Core elements 1.1 namespace.</summary>
    public const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    // Imports first: the schema set resolves xs:import by namespace once the imported schema is present,
    // so nothing is fetched from the schemaLocation URLs.
    private static readonly string[] ResourceNames =
    [
        "xml.xsd",
        "simpledc20021212.xsd",
        "oai_dc.xsd",
        "OAI-PMH.xsd",
    ];

    /// <summary>Creates a compiled schema set with OAI-PMH 2.0, <c>oai_dc</c>, Dublin Core 1.1 and the <c>xml</c> namespace schema.</summary>
    public static XmlSchemaSet CreateSchemaSet()
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

        foreach (var name in ResourceNames)
        {
            using var stream = OpenSchema(name);
            using var reader = XmlReader.Create(stream, readerSettings);
            set.Add(XmlSchema.Read(reader, null)!);
        }

        set.Compile();
        return set;
    }

    /// <summary>Opens one of the embedded schema files by file name, for example <c>OAI-PMH.xsd</c>.</summary>
    public static Stream OpenSchema(string fileName)
    {
        var resource = $"{typeof(OaiPmhSchemas).Namespace}.{fileName}";
        return typeof(OaiPmhSchemas).Assembly.GetManifestResourceStream(resource)
            ?? throw new ArgumentException($"No embedded schema named '{fileName}'.", nameof(fileName));
    }
}
