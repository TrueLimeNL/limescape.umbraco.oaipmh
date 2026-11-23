using System.Globalization;
using System.Xml.Linq;
using Limescape.Umbraco.OaiPmh.Configuration;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Schemas;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace Limescape.Umbraco.OaiPmh.Tests;

/// <summary>Tombstones at protocol level (deleted header in every record verb) and POST parity.</summary>
public sealed class TombstoneAndPostParityTests
{
    private static readonly XNamespace Oai = OaiPmhSchemas.OaiPmhNamespace;
    private static readonly string Later = RecordStatePolicy.FormatDate(EndpointTests.Now.UtcDateTime.AddSeconds(10));

    /// <summary>Three records; the second was deleted ten seconds later, which advanced its datestamp.</summary>
    private static EndpointTests.HarvestData WithTombstone()
    {
        var data = new EndpointTests.HarvestData(3);
        data.Rows[1] = data.Rows[1] with { Deleted = true, Datestamp = EndpointTests.Now.UtcDateTime.AddSeconds(10), Revision = 2 };
        data.Content.Remove(data.Rows[1].Key);
        data.Rows.Sort((a, b) => a.Datestamp.CompareTo(b.Datestamp) is var c and not 0 ? c : a.Id.CompareTo(b.Id));
        return data;
    }

    [Theory]
    [InlineData("ListIdentifiers")]
    [InlineData("ListRecords")]
    public async Task Incremental_harvest_returns_the_tombstone_with_its_new_datestamp(string verb)
    {
        var data = WithTombstone();
        await using var app = await EndpointTests.StartAsync(data: data);
        var doc = await EndpointTests.ReadAsync(await app.GetTestClient().GetAsync(
            new Uri($"/oai?verb={verb}&metadataPrefix=oai_dc&from={Later}", UriKind.Relative), TestContext.Current.CancellationToken));
        var header = Assert.Single(doc.Descendants(Oai + "header"));
        Assert.Equal("deleted", header.Attribute("status")!.Value);
        Assert.Equal(data.Rows.Single(r => r.Deleted).Identifier, header.Element(Oai + "identifier")!.Value);
        Assert.Equal(Later, header.Element(Oai + "datestamp")!.Value);
        Assert.Empty(doc.Descendants(Oai + "metadata"));
    }

    [Fact]
    public async Task GetRecord_of_a_tombstone_has_a_deleted_header_and_no_metadata_while_formats_remain_listed()
    {
        var data = WithTombstone();
        await using var app = await EndpointTests.StartAsync(data: data);
        var client = app.GetTestClient();
        var id = Uri.EscapeDataString(data.Rows.Single(r => r.Deleted).Identifier);
        var record = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?verb=GetRecord&metadataPrefix=oai_dc&identifier=" + id, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("deleted", record.Descendants(Oai + "header").Single().Attribute("status")!.Value);
        Assert.Empty(record.Descendants(Oai + "metadata"));
        var formats = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?verb=ListMetadataFormats&identifier=" + id, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("oai_dc", formats.Descendants(Oai + "metadataPrefix").Single().Value);
        var identify = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?verb=Identify", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("persistent", identify.Descendants(Oai + "deletedRecord").Single().Value);
    }

    [Theory]
    [InlineData("ListIdentifiers")]
    [InlineData("ListRecords")]
    [InlineData("GetRecord")]
    public async Task Stale_active_row_of_content_that_is_no_longer_public_is_served_as_deleted(string verb)
    {
        var data = new EndpointTests.HarvestData(1);
        data.Content.Clear(); // The row still says active; the content is gone or private before the consistency job ran.
        await using var app = await EndpointTests.StartAsync(data: data);
        var query = verb == "GetRecord"
            ? "verb=GetRecord&metadataPrefix=oai_dc&identifier=" + Uri.EscapeDataString(data.Rows[0].Identifier)
            : $"verb={verb}&metadataPrefix=oai_dc";
        var doc = await EndpointTests.ReadAsync(await app.GetTestClient().GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("deleted", doc.Descendants(Oai + "header").Single().Attribute("status")!.Value);
        Assert.Empty(doc.Descendants(Oai + "metadata"));
    }

    [Theory]
    [InlineData("ListIdentifiers", "")]
    [InlineData("ListRecords", "")]
    [InlineData("ListRecords", "&from=2026-10-08T12:34:56Z&until=2026-10-08T12:35:30Z")]
    [InlineData("ListIdentifiers", "&from=2026-10-08&until=2026-10-08")]
    public async Task Paged_harvest_by_POST_equals_GET_page_by_page(string verb, string bounds)
    {
        var data = WithTombstone();
        foreach (var i in Enumerable.Range(0, 4))
        {
            var key = Guid.NewGuid();
            data.Rows.Add(new(100 + i, key, "oai:test:" + key, EndpointTests.Now.UtcDateTime.AddSeconds(20 + i), false, "", 1));
            data.Content[key] = new(key, [new("title", "title", "Later " + i)], "/materials/" + key);
        }
        await using var app = await EndpointTests.StartAsync(new() { ["Limescape:OaiPmh:PageSize"] = "2" }, data);
        var client = app.GetTestClient();
        var arguments = new Dictionary<string, string> { ["verb"] = verb, ["metadataPrefix"] = "oai_dc" };
        foreach (var bound in bounds.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Split('=')))
        {
            arguments[bound[0]] = bound[1];
        }

        var pages = 0;
        var headers = 0;
        while (true)
        {
            var query = string.Join('&', arguments.Select(a => a.Key + "=" + Uri.EscapeDataString(a.Value)));
            var get = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
            using var form = new FormUrlEncodedContent(arguments);
            var post = await EndpointTests.ReadAsync(await client.PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
            Assert.Equal(get.ToString(), post.ToString());
            pages++;
            headers += get.Descendants(Oai + "header").Count();
            var token = get.Descendants(Oai + "resumptionToken").SingleOrDefault()?.Value;
            if (string.IsNullOrEmpty(token))
            {
                break;
            }

            arguments = new() { ["verb"] = verb, ["resumptionToken"] = token };
        }

        Assert.Equal(7, headers);
        Assert.Equal(4, pages);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded; charset=utf-8")]
    [InlineData("application/x-www-form-urlencoded;charset=UTF-8")]
    public async Task POST_accepts_a_charset_parameter(string contentType)
    {
        var data = WithTombstone();
        await using var app = await EndpointTests.StartAsync(data: data);
        var client = app.GetTestClient();
        var query = "verb=GetRecord&metadataPrefix=oai_dc&identifier=" + Uri.EscapeDataString(data.Rows[0].Identifier);
        var get = await EndpointTests.ReadAsync(await client.GetAsync(new Uri("/oai?" + query, UriKind.Relative), TestContext.Current.CancellationToken));
        using var form = new StringContent(query);
        form.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        var post = await EndpointTests.ReadAsync(await client.PostAsync(new Uri("/oai", UriKind.Relative), form, TestContext.Current.CancellationToken));
        Assert.Equal(get.ToString(), post.ToString());
        Assert.NotEmpty(post.Descendants(Oai + "metadata"));
    }

    [Theory]
    [InlineData("/oai?verb=Identify", "application/x-www-form-urlencoded", "verb=Identify")]
    [InlineData("/oai?metadataPrefix=oai_dc", "application/x-www-form-urlencoded", "verb=ListRecords")]
    [InlineData("/oai", "text/plain", "verb=Identify")]
    [InlineData("/oai", "application/json", "{\"verb\":\"Identify\"}")]
    [InlineData("/oai", "multipart/form-data; boundary=x", "--x\r\nContent-Disposition: form-data; name=\"verb\"\r\n\r\nIdentify\r\n--x--\r\n")]
    public async Task POST_with_query_arguments_or_another_body_type_is_badArgument(string path, string contentType, string body)
    {
        await using var app = await EndpointTests.StartAsync();
        using var content = new StringContent(body);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        var doc = await EndpointTests.ReadAsync(await app.GetTestClient().PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken));
        Assert.Equal("badArgument", doc.Root!.Element(Oai + "error")!.Attribute("code")!.Value);
        Assert.Empty(doc.Root.Element(Oai + "request")!.Attributes());
    }

    [Theory]
    [InlineData("12:34:56", "02:00:00", "13:25:04")]
    [InlineData("12:34:56", "13:00:00", "00:25:04")]
    [InlineData("12:34:56", "12:34:56", "1.00:00:00")]
    public void ConsistencyJob_runs_at_the_next_configured_UTC_time(string now, string at, string expected)
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero) + TimeSpan.Parse(now, CultureInfo.InvariantCulture));
        var options = Options.Create(new OaiPmhOptions { ConsistencyJobTime = TimeSpan.Parse(at, CultureInfo.InvariantCulture) });
        var job = new ConsistencyJob(null!, null!, null!, null!, options, clock, null!);
        Assert.Equal(TimeSpan.Parse(expected, CultureInfo.InvariantCulture), job.Delay);
        Assert.Equal(TimeSpan.FromDays(1), job.Period);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
