using System.Net;
using System.Xml.Linq;
using Limescape.Umbraco.OaiPmh.Endpoint;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Schemas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Limescape.Umbraco.OaiPmh.Tests;

public sealed class EndpointTests
{
    private static readonly XNamespace Oai = OaiPmhSchemas.OaiPmhNamespace;
    internal static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 34, 56, TimeSpan.Zero);

    [Theory]
    [InlineData("", "badVerb")]
    [InlineData("verb=Unknown", "badVerb")]
    [InlineData("verb=Identify&verb=Identify", "badVerb")]
    [InlineData("verb=Identify&x=1", "badArgument")]
    [InlineData("verb=Identify&metadataPrefix=oai_dc", "badArgument")]
    [InlineData("verb=GetRecord&identifier=x", "badArgument")]
    [InlineData("verb=GetRecord&metadataPrefix=oai_dc&identifier=x", "idDoesNotExist")]
    [InlineData("verb=GetRecord&metadataPrefix=oai_dc&identifier=%01", "badArgument")]
    [InlineData("verb=ListRecords", "badArgument")]
    [InlineData("verb=ListRecords&metadataPrefix=lom", "cannotDisseminateFormat")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc&metadataPrefix=oai_dc", "badArgument")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc&from=bad", "badArgument")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc&from=2026-10-09&until=2026-10-08", "badArgument")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc&from=2026-10-08&until=2026-10-08T12:00:00Z", "badArgument")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc&set=origin:internal", "noSetHierarchy")]
    [InlineData("verb=ListRecords&resumptionToken=invalid", "badResumptionToken")]
    [InlineData("verb=ListRecords&resumptionToken=invalid&metadataPrefix=oai_dc", "badArgument")]
    [InlineData("verb=ListSets", "noSetHierarchy")]
    [InlineData("verb=ListMetadataFormats&identifier=unknown", "idDoesNotExist")]
    public async Task Protocol_errors_validate_for_GET_and_POST(string query, string expected)
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var get = await ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal(expected, get.Root!.Element(Oai + "error")!.Attribute("code")!.Value);
        if (expected is "badArgument" or "badVerb")
        {
            Assert.Empty(get.Root.Element(Oai + "request")!.Attributes());
        }
        using var form = new StringContent(query, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        var post = await ReadAsync(await client.PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
        Assert.Equal(get.ToString(), post.ToString());
    }

    [Theory]
    [InlineData("verb=Identify", "Identify")]
    [InlineData("verb=ListMetadataFormats", "ListMetadataFormats")]
    [InlineData("verb=ListIdentifiers&metadataPrefix=oai_dc", "ListIdentifiers")]
    [InlineData("verb=ListRecords&metadataPrefix=oai_dc", "ListRecords")]
    public async Task Successful_verbs_validate_and_POST_matches_GET(string query, string verb)
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var get = await ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.NotNull(get.Root!.Element(Oai + verb));
        using var form = new StringContent(query, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        var post = await ReadAsync(await client.PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
        Assert.Equal(get.ToString(), post.ToString());
    }

    [Fact]
    public async Task GetRecord_contains_escaped_mapped_fields_and_absolute_landing_page()
    {
        var data = new HarvestData(1);
        await using var app = await StartAsync(data: data);
        var id = data.Rows[0].Identifier;
        var document = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?verb=GetRecord&metadataPrefix=oai_dc&identifier=" + id, UriKind.Relative), TestContext.Current.CancellationToken));
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.Equal("Title <&> 0", Assert.Single(document.Descendants(dc + "title")).Value);
        Assert.Equal("https://example.org/materials/" + data.Rows[0].Key, Assert.Single(document.Descendants(dc + "identifier")).Value);
        Assert.Equal("CC-BY-4.0", Assert.Single(document.Descendants(dc + "rights")).Value);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["verb"] = "GetRecord", ["metadataPrefix"] = "oai_dc", ["identifier"] = id });
        var post = await ReadAsync(await app.GetTestClient().PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
        Assert.Equal(document.ToString(), post.ToString());
    }

    [Theory]
    [InlineData(2500)]
    [InlineData(50000)]
    public async Task Full_harvest_returns_every_record_once_with_valid_cursor(int count)
    {
        var data = new HarvestData(count);
        await using var app = await StartAsync(data: data);
        var client = app.GetTestClient();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var query = "verb=ListRecords&metadataPrefix=oai_dc";
        var offset = 0;
        while (true)
        {
            var document = await ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
            var records = document.Descendants(Oai + "record").ToArray();
            Assert.InRange(records.Length, 1, 100);
            foreach (var id in records.Select(x => x.Element(Oai + "header")!.Element(Oai + "identifier")!.Value))
            {
                Assert.True(identifiers.Add(id), "Duplicate identifier: " + id);
            }
            var token = document.Descendants(Oai + "resumptionToken").SingleOrDefault();
            if (token is not null)
            {
                Assert.Equal(offset.ToString(System.Globalization.CultureInfo.InvariantCulture), token.Attribute("cursor")!.Value);
                Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), token.Attribute("completeListSize")!.Value);
            }
            offset += records.Length;
            if (token is null || token.Value.Length == 0)
            {
                break;
            }
            Assert.NotNull(token.Attribute("expirationDate"));
            query = "verb=ListRecords&resumptionToken=" + Uri.EscapeDataString(token.Value);
        }
        Assert.Equal(count, identifiers.Count);
    }

    [Theory]
    [InlineData("from=2026-10-08", 3)]
    [InlineData("until=2026-10-08", 3)]
    [InlineData("from=2026-10-08T12:34:57Z", 2)]
    [InlineData("until=2026-10-08T12:34:57Z", 2)]
    [InlineData("from=2026-10-08T12:34:57Z&until=2026-10-08T12:34:57Z", 1)]
    public async Task Date_filters_are_inclusive(string bounds, int expected)
    {
        await using var app = await StartAsync(data: new(3, staggered: true));
        var doc = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?verb=ListIdentifiers&metadataPrefix=oai_dc&" + bounds, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal(expected, doc.Descendants(Oai + "header").Count());
    }

    [Fact]
    public async Task Empty_repository_returns_noRecordsMatch()
    {
        await using var app = await StartAsync(data: new(0));
        var doc = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?verb=ListRecords&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("noRecordsMatch", doc.Root!.Element(Oai + "error")!.Attribute("code")!.Value);
    }

    [Fact]
    public async Task Changed_result_set_and_cross_verb_tokens_are_rejected()
    {
        var data = new HarvestData(101);
        await using var app = await StartAsync(data: data);
        var client = app.GetTestClient();
        var doc = await ReadAsync(await client.GetAsync(new Uri("/oai?verb=ListRecords&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken));
        var token = doc.Descendants(Oai + "resumptionToken").Single().Value;
        foreach (var verb in new[] { "ListIdentifiers", "ListRecords" })
        {
            if (verb == "ListRecords")
            {
                data.Version++;
            }
            var error = await ReadAsync(await client.GetAsync(new Uri("/oai?verb=" + verb + "&resumptionToken=" + Uri.EscapeDataString(token), UriKind.Relative), TestContext.Current.CancellationToken));
            Assert.Equal("badResumptionToken", error.Root!.Element(Oai + "error")!.Attribute("code")!.Value);
        }
    }

    [Fact]
    public async Task Deleted_record_and_currently_private_content_have_no_metadata()
    {
        var data = new HarvestData(2);
        data.Rows[0] = data.Rows[0] with { Deleted = true };
        data.Content.Clear();
        await using var app = await StartAsync(data: data);
        var doc = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?verb=ListRecords&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Empty(doc.Descendants(Oai + "metadata"));
        Assert.All(doc.Descendants(Oai + "header"), h => Assert.Equal("deleted", h.Attribute("status")!.Value));
    }

    [Fact]
    public async Task Page_size_adapts_to_XML_byte_budget()
    {
        var data = new HarvestData(3);
        foreach (var key in data.Content.Keys.ToArray())
        {
            data.Content[key] = data.Content[key] with { Fields = [new("description", "description", new string('a', 5000))] };
        }
        await using var app = await StartAsync(new() { ["Limescape:OaiPmh:MaxPageBytes"] = "8192" }, data);
        var doc = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?verb=ListRecords&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Single(doc.Descendants(Oai + "record"));
        Assert.NotEmpty(doc.Descendants(Oai + "resumptionToken").Single().Value);
    }

    [Fact]
    public async Task Rate_limit_returns_503_RetryAfter_and_is_per_IP()
    {
        await using var app = await StartAsync(new() { ["Limescape:OaiPmh:RequestsPerSecond"] = "1" });
        var server = app.GetTestServer();
        async Task<int> Request(string address)
        {
            var context = await server.SendAsync(c =>
            {
                c.Connection.RemoteIpAddress = IPAddress.Parse(address);
                c.Request.Path = "/oai";
                c.Request.QueryString = new("?verb=Identify");
            }, TestContext.Current.CancellationToken);
            if (context.Response.StatusCode == 503)
            {
                Assert.Equal("1", context.Response.Headers.RetryAfter);
            }
            return context.Response.StatusCode;
        }
        Assert.Equal(200, await Request("192.0.2.1"));
        Assert.Equal(503, await Request("192.0.2.1"));
        Assert.Equal(200, await Request("192.0.2.2"));
    }

    [Fact]
    public async Task Other_HTTP_methods_are_not_served()
    {
        await using var app = await StartAsync();
        using var response = await app.GetTestClient().DeleteAsync(new Uri("/oai", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Configured_endpoint_and_baseURL_are_used()
    {
        await using var app = await StartAsync(new()
        {
            ["Limescape:OaiPmh:EndpointPath"] = "/harvest",
            ["Limescape:OaiPmh:BaseUrl"] = "https://custom.example.org/harvest"
        });
        var doc = await ReadAsync(await app.GetTestClient().GetAsync(new Uri("/harvest?verb=Identify", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("https://custom.example.org/harvest", doc.Root!.Element(Oai + "request")!.Value);
        Assert.Equal("https://custom.example.org/harvest", doc.Descendants(Oai + "baseURL").Single().Value);
    }

    [Fact]
    public async Task Disabled_tenant_endpoint_returns_404()
    {
        await using var app = await StartAsync(new() { ["Limescape:OaiPmh:Enabled"] = "false" });
        using var response = await app.GetTestClient().GetAsync(new Uri("/oai?verb=Identify", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_can_be_replayed_and_resume_ListIdentifiers()
    {
        await using var app = await StartAsync(new() { ["Limescape:OaiPmh:PageSize"] = "1" }, new(3));
        var client = app.GetTestClient();
        var first = await ReadAsync(await client.GetAsync(new Uri("/oai?verb=ListIdentifiers&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken));
        var token = first.Descendants(Oai + "resumptionToken").Single().Value;
        var query = new Uri("/oai?verb=ListIdentifiers&resumptionToken=" + Uri.EscapeDataString(token), UriKind.Relative);
        var next = await ReadAsync(await client.GetAsync(query, TestContext.Current.CancellationToken));
        var replay = await ReadAsync(await client.GetAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal(next.ToString(), replay.ToString());
        Assert.NotEqual(first.Descendants(Oai + "identifier").Single().Value, next.Descendants(Oai + "identifier").Single().Value);
    }

    [Fact]
    public async Task Unsplittable_oversized_record_returns_503_without_partial_XML()
    {
        var data = new HarvestData(1);
        var key = data.Rows[0].Key;
        data.Content[key] = data.Content[key] with { Fields = [new("description", "description", new string('a', 10_000_000))] };
        await using var app = await StartAsync(data: data);
        using var response = await app.GetTestClient().GetAsync(new Uri("/oai?verb=ListRecords&metadataPrefix=oai_dc", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.RetryAfter!.Delta);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    internal static async Task<XDocument> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/xml", response.Content.Headers.ContentType?.MediaType);
            return XsdValidator.AssertValid(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    internal static async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null, HarvestData? data = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Limescape:OaiPmh:RequestsPerSecond"] = "10000",
            ["Limescape:OaiPmh:BaseUrl"] = "https://example.org/oai",
            ["Limescape:OaiPmh:TokenSigningKey"] = Convert.ToBase64String(new byte[32])
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        builder.Services.AddOaiPmh(builder.Configuration);
        data ??= new(2);
        builder.Services.AddSingleton<IRecordStateStore>(data);
        builder.Services.AddSingleton<IContentSelector>(data);
        var app = builder.Build();
        app.MapOaiPmh();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class HarvestData : IRecordStateStore, IContentSelector
    {
        public List<OaiRecord> Rows { get; } = [];
        public Dictionary<Guid, ExportContent> Content { get; } = [];
        public int Version { get; set; } = 1;
        public HarvestData(int count, bool staggered = false)
        {
            for (var i = 0; i < count; i++)
            {
                var key = Guid.NewGuid();
                Rows.Add(new(i + 1, key, "oai:test:" + key, Now.UtcDateTime.AddSeconds(staggered ? i : 0), false, "", 1));
                Content[key] = new(key, [new("title", "title", "Title <&> " + i), new("rights", "license", "CC-BY-4.0")], "/materials/" + key);
            }
        }
        public OaiRecord? Find(string identifier) => Rows.Find(x => x.Identifier == identifier);
        public DateTime? Earliest() => Rows.MinBy(x => x.Datestamp)?.Datestamp;
        public RecordPage Page(DateTime? from, DateTime? until, DateTime? after, long afterId, int take)
        {
            var matches = Rows.Where(x => (from is null || x.Datestamp >= from) && (until is null || x.Datestamp <= until)).ToArray();
            return new(matches.Where(x => after is null || x.Datestamp > after || (x.Datestamp == after && x.Id > afterId)).Take(take).ToArray(),
                matches.Length, Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public RecordChange Update(Guid key, ExportContent? content) => throw new NotSupportedException();
        public IReadOnlyCollection<Guid> ActiveKeys() => Rows.Where(x => !x.Deleted).Select(x => x.Key).ToArray();
        public ExportContent? GetExport(Guid key) => Content.GetValueOrDefault(key);
    }
}
