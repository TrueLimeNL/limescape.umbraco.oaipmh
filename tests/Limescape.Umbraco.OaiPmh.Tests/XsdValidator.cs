using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Limescape.Umbraco.OaiPmh.Schemas;

namespace Limescape.Umbraco.OaiPmh.Tests;

/// <summary>Validates XML strings against the embedded OAI-PMH 2.0, oai_dc and Dublin Core schemas.</summary>
public static class XsdValidator
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(OaiPmhSchemas.CreateSchemaSet);

    /// <summary>Returns all validation errors and warnings; empty when the document is valid.</summary>
    public static IReadOnlyList<string> Validate(string xml)
    {
        var problems = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
        };
        settings.ValidationEventHandler += (_, e) =>
            problems.Add($"{e.Severity} at {e.Exception.LineNumber}:{e.Exception.LinePosition}: {e.Message}");

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        try
        {
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            problems.Add($"Not well-formed: {ex.Message}");
        }

        return problems;
    }

    /// <summary>Fails the test with all problems when the document does not validate; returns the parsed document.</summary>
    public static XDocument AssertValid(string xml)
    {
        var problems = Validate(xml);
        Assert.True(problems.Count == 0, $"XSD validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}{Environment.NewLine}{xml}");
        return XDocument.Parse(xml);
    }
}
