namespace Limescape.Umbraco.OaiPmh.Tests;

/// <summary>The validator itself must reject invalid documents, or every XSD assertion in this suite is meaningless.</summary>
public sealed class XsdValidatorTests
{
    private static string Envelope(string body) => $$"""
        <OAI-PMH xmlns="http://www.openarchives.org/OAI/2.0/"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:schemaLocation="http://www.openarchives.org/OAI/2.0/ http://www.openarchives.org/OAI/2.0/OAI-PMH.xsd">
          <responseDate>2026-10-08T10:00:00Z</responseDate>
          {{body}}
        </OAI-PMH>
        """;

    private static string GetRecordWithDublinCore(string extraElement) => $$"""
        <request verb="GetRecord" identifier="oai:example.org:1" metadataPrefix="oai_dc">https://example.org/oai</request>
        <GetRecord>
          <record>
            <header>
              <identifier>oai:example.org:1</identifier>
              <datestamp>2026-10-08T09:00:00Z</datestamp>
            </header>
            <metadata>
              <oai_dc:dc xmlns:oai_dc="http://www.openarchives.org/OAI/2.0/oai_dc/" xmlns:dc="http://purl.org/dc/elements/1.1/"
                         xsi:schemaLocation="http://www.openarchives.org/OAI/2.0/oai_dc/ http://www.openarchives.org/OAI/2.0/oai_dc.xsd">
                <dc:title xml:lang="en">A learning material</dc:title>
                {{extraElement}}
              </oai_dc:dc>
            </metadata>
          </record>
        </GetRecord>
        """;

    [Fact]
    public void Accepts_a_valid_record_in_oai_dc()
    {
        var xml = Envelope(GetRecordWithDublinCore("<dc:creator>Someone</dc:creator>"));

        Assert.Empty(XsdValidator.Validate(xml));
    }

    [Fact]
    public void Rejects_an_unknown_error_code()
    {
        var xml = Envelope("""<request>https://example.org/oai</request><error code="notAnError">x</error>""");

        Assert.NotEmpty(XsdValidator.Validate(xml));
    }

    [Fact]
    public void Rejects_a_response_without_request_element()
    {
        var xml = Envelope("""<error code="badVerb">x</error>""");

        Assert.NotEmpty(XsdValidator.Validate(xml));
    }

    [Fact]
    public void Rejects_an_element_outside_dublin_core_inside_oai_dc()
    {
        // Proves the oai_dc and dc schemas are loaded: OAI-PMH validates metadata strictly.
        var xml = Envelope(GetRecordWithDublinCore("<dc:notAnElement>x</dc:notAnElement>"));

        Assert.NotEmpty(XsdValidator.Validate(xml));
    }

    [Fact]
    public void Rejects_xml_that_is_not_well_formed()
    {
        Assert.NotEmpty(XsdValidator.Validate("<OAI-PMH xmlns=\"http://www.openarchives.org/OAI/2.0/\">"));
    }
}
