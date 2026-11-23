using System.Xml.Linq;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Schemas;
using Microsoft.AspNetCore.TestHost;

namespace Limescape.Umbraco.OaiPmh.Tests;

/// <summary>
/// A hostile mapping routes every confidential value into Dublin Core and a faulty selector supplies them anyway.
/// The writer must still emit none of them, in any verb, in every advertised format, for GET and POST.
/// </summary>
public sealed class LeakTests
{
    private static readonly XNamespace Oai = OaiPmhSchemas.OaiPmhNamespace;
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private static readonly Dictionary<string, string?> Hostile = new()
    {
        ["Limescape:OaiPmh:PageSize"] = "1",
        ["Limescape:OaiPmh:NonExportableFields:0"] = "internalNotes",
        ["Limescape:OaiPmh:DublinCoreMapping:title:0"] = "title",
        ["Limescape:OaiPmh:DublinCoreMapping:description:0"] = "internalNotes",
        ["Limescape:OaiPmh:DublinCoreMapping:description:1"] = "confidential",
        ["Limescape:OaiPmh:DublinCoreMapping:format:0"] = "elements",
        ["Limescape:OaiPmh:DublinCoreMapping:relation:0"] = "elementRef",
    };

    private static EndpointTests.HarvestData Data()
    {
        var data = new EndpointTests.HarvestData(3);
        data.Rows[2] = data.Rows[2] with { Deleted = true };
        foreach (var (key, index) in data.Rows.Select((row, index) => (row.Key, index)))
        {
            data.Content[key] = new(key,
            [
                new("title", "title", "Public " + index),
                new("description", "internalNotes", "SECRET-notes"),
                new("description", "INTERNALNOTES", "SECRET-notes-case"),
                new("description", "confidential", "SECRET-flag"),
                new("format", "elements", """[{"name":"SECRET-element.pdf","url":"/media/x/SECRET-element.pdf","size":1234}]"""),
                new("format", "elements", """ {"mediaKey":"SECRET-key"}"""),
                new("relation", "elementRef", "umb://media/5ECE7000000000000000000000000000"),
                new("description", "unmapped", "SECRET-unmapped"),
                new("bogus", "title", "SECRET-bogus"),
            ], "/materials/" + key);
        }
        return data;
    }

    [Fact]
    public async Task No_confidential_or_non_exportable_value_in_any_verb_format_or_method()
    {
        var data = Data();
        await using var app = await EndpointTests.StartAsync(new(Hostile), data);
        var client = app.GetTestClient();
        var responses = new List<XDocument>();

        async Task<XDocument> Harvest(string query)
        {
            var get = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
            using var form = new StringContent(query, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var post = await EndpointTests.ReadAsync(await client.PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
            Assert.Equal(get.ToString(), post.ToString());
            responses.Add(get);
            return get;
        }

        await Harvest("verb=Identify");
        await Harvest("verb=ListSets");
        var formats = (await Harvest("verb=ListMetadataFormats")).Descendants(Oai + "metadataPrefix").Select(x => x.Value).ToArray();
        Assert.Contains("oai_dc", formats);
        foreach (var prefix in formats.Concat(["lom", "nl_lom"]))
        {
            foreach (var row in data.Rows)
            {
                var id = Uri.EscapeDataString(row.Identifier);
                await Harvest("verb=ListMetadataFormats&identifier=" + id);
                await Harvest($"verb=GetRecord&metadataPrefix={prefix}&identifier={id}");
            }

            foreach (var verb in new[] { "ListIdentifiers", "ListRecords" })
            {
                var query = $"verb={verb}&metadataPrefix={prefix}";
                while (true)
                {
                    var token = (await Harvest(query)).Descendants(Oai + "resumptionToken").SingleOrDefault()?.Value;
                    if (string.IsNullOrEmpty(token))
                    {
                        break;
                    }

                    query = $"verb={verb}&resumptionToken={Uri.EscapeDataString(token)}";
                }
            }
        }

        foreach (var xml in responses.Select(r => r.ToString()))
        {
            Assert.DoesNotContain("SECRET", xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/media/", xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("umb://", xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("5ECE7", xml, StringComparison.OrdinalIgnoreCase);
        }

        // Not vacuous: public metadata of both live records was harvested, the tombstone never carried metadata.
        var titles = responses.SelectMany(r => r.Descendants(Dc + "title")).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["Public 0", "Public 1"], titles.Order(StringComparer.Ordinal));
        Assert.Empty(responses.SelectMany(r => r.Descendants(Dc + "description")));
        Assert.Empty(responses.SelectMany(r => r.Descendants(Dc + "format")));
        Assert.Empty(responses.SelectMany(r => r.Descendants(Dc + "relation")));
    }

    [Theory]
    [InlineData("umb://media/5ece7000000000000000000000000000")]
    [InlineData("  UMB://document/5ece7000000000000000000000000000")]
    [InlineData(" [\"umb://media/x\"]")]
    public void Element_references_never_pass_as_scalar_values(string value) => Assert.Null(ContentSelector.Scalar(value));

    [Fact]
    public void Confidential_flag_and_non_exportable_fields_are_excluded_case_insensitively()
    {
        var options = new Configuration.OaiPmhOptions { NonExportableFields = ["internalNotes"] };
        Assert.True(ContentSelector.IsExcluded("INTERNALNOTES", options));
        Assert.True(ContentSelector.IsExcluded("Confidential", options));
        Assert.False(ContentSelector.IsExcluded("title", options));
        options.ConfidentialAlias = "";
        Assert.False(ContentSelector.IsExcluded("confidential", options));
    }
}
