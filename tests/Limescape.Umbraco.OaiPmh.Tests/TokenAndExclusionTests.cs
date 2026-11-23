using Limescape.Umbraco.OaiPmh.Configuration;
using Limescape.Umbraco.OaiPmh.Endpoint;
using Limescape.Umbraco.OaiPmh.Formats;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Tokens;
using Microsoft.Extensions.Options;

namespace Limescape.Umbraco.OaiPmh.Tests;

public sealed class TokenAndExclusionTests
{
    [Fact]
    public void Tokens_reject_tampering_expiry_and_configuration_changes()
    {
        var time = new MovingTime();
        var options = new OaiPmhOptions { TokenSigningKey = Convert.ToBase64String(new byte[32]) };
        var service = new ResumptionTokenService(Options.Create(options), time);
        var cursor = new HarvestCursor("ListRecords", "oai_dc", null, null, time.Now.UtcDateTime, 1, 100, 200, "1", time.Now.AddHours(24), service.Configuration);
        var token = service.Encode(cursor);
        Assert.Equal(cursor, service.Decode(token, "ListRecords"));
        Assert.Throws<OaiException>(() => service.Decode("A" + token, "ListRecords"));
        options.NonExportableFields = ["description"];
        var changed = new ResumptionTokenService(Options.Create(options), time);
        Assert.Throws<OaiException>(() => changed.Decode(token, "ListRecords"));
        time.Now = time.Now.AddHours(24);
        Assert.Throws<OaiException>(() => service.Decode(token, "ListRecords"));
    }

    [Fact]
    public void Writer_enforces_excluded_fields_even_if_a_selector_supplies_them()
    {
        var options = new OaiPmhOptions { NonExportableFields = ["description"] };
        var content = new ExportContent(Guid.NewGuid(), [new("title", "title", "Public"),
            new("description", "description", "SECRET"), new("description", "unmapped", "SECRET2"),
            new("illegal", "title", "SECRET3")], "/materials/1");
        var xml = OaiDcWriter.Write(content, options, new Uri("https://example.org/oai")).ToString();
        Assert.Contains("Public", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"confidential\":true,\"url\":\"secret\"}")]
    [InlineData("[{\"name\":\"secret\"}]")]
    public void Structured_editor_values_cannot_leak_through_scalar_mapping(string value) =>
        Assert.Null(ContentSelector.Scalar(value));

    [Fact]
    public void XML_text_removes_illegal_control_characters_and_preserves_Unicode()
    {
        Assert.Equal("title 😀", ContentSelector.XmlText("title\u0001 😀"));
    }

    [Fact]
    public void Hash_changes_only_when_export_changes()
    {
        var content = new ExportContent(Guid.NewGuid(), [new("title", "title", "Public")], "/materials/1");
        Assert.Equal(RecordStatePolicy.Hash(content), RecordStatePolicy.Hash(content with { }));
        Assert.NotEqual(RecordStatePolicy.Hash(content), RecordStatePolicy.Hash(content with { Fields = [new("title", "title", "Changed")] }));
    }

    private sealed class MovingTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
