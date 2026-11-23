using System.Text;
using System.Xml;
using System.Xml.Linq;
using Limescape.Umbraco.OaiPmh.Configuration;
using Limescape.Umbraco.OaiPmh.Formats;
using Limescape.Umbraco.OaiPmh.Records;
using Limescape.Umbraco.OaiPmh.Schemas;
using Limescape.Umbraco.OaiPmh.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Limescape.Umbraco.OaiPmh.Endpoint;

public static partial class OaiPmhEndpoint
{
    public const string DisplayName = "OAI-PMH";
    private static readonly XNamespace Oai = OaiPmhSchemas.OaiPmhNamespace;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static IEndpointConventionBuilder MapOaiPmh(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<OaiPmhOptions>>().Value;
        return endpoints.MapMethods(options.EndpointPath, [HttpMethods.Get, HttpMethods.Post], HandleAsync).WithDisplayName(DisplayName);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<OaiPmhOptions>>().Value;
        if (!options.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        using var lease = services.GetRequiredService<RateLimiting>().Acquire(context);
        if (!lease.IsAcquired)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            return;
        }
        var time = services.GetRequiredService<TimeProvider>();
        var url = RequestUrl(context.Request, options);
        OaiRequest? request = null;
        XElement result;
        try
        {
            request = await RequestParser.ParseAsync(context.Request);
            var store = services.GetRequiredService<IRecordStateStore>();
            var selector = services.GetRequiredService<IContentSelector>();
            var tokens = services.GetRequiredService<ResumptionTokenService>();
            result = Execute(request, options, url, time, store, selector, tokens);
        }
        catch (OaiException error)
        {
            if (error.Code is OaiPmhErrorCode.BadArgument or OaiPmhErrorCode.BadVerb)
            {
                request = null;
            }

            result = new XElement(Oai + "error", new XAttribute("code", error.Code.ToProtocolValue()), error.Message);
        }
        var requestElement = new XElement(Oai + "request", url);
        if (request is not null)
        {
            foreach (var arg in request.Arguments)
            {
                requestElement.Add(new XAttribute(arg.Key, arg.Value));
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Oai + "OAI-PMH", new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                new XAttribute(Xsi + "schemaLocation", $"{Oai} {OaiPmhSchemas.OaiPmhSchemaLocation}"),
                new XElement(Oai + "responseDate", ErrorWriter.FormatResponseDate(time.GetUtcNow())), requestElement, result));
        // A single record cannot be split. Fail without a partial XML response if it exceeds the protocol page budget.
        if (Encoding.UTF8.GetByteCount(document.ToString(SaveOptions.DisableFormatting)) > 9_900_000)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogOversizedResponse(services.GetRequiredService<ILoggerFactory>().CreateLogger("Limescape.Umbraco.OaiPmh"), request?.Verb);
            context.Response.Headers.RetryAfter = "60";
            return;
        }
        context.Response.ContentType = "text/xml; charset=utf-8";
        await using var xml = XmlWriter.Create(context.Response.Body, ErrorWriter.WriterSettings);
        await document.WriteToAsync(xml, context.RequestAborted);
        await xml.FlushAsync();
    }

    private static XElement Execute(OaiRequest request, OaiPmhOptions options, string url, TimeProvider time,
        IRecordStateStore store, IContentSelector selector, ResumptionTokenService tokens)
    {
        var verb = request.Verb;
        if (verb == "Identify")
        {
            return new XElement(Oai + verb,
                new XElement(Oai + "repositoryName", string.IsNullOrWhiteSpace(options.RepositoryName) ? "Learning materials" : options.RepositoryName),
                new XElement(Oai + "baseURL", url), new XElement(Oai + "protocolVersion", "2.0"),
                new XElement(Oai + "adminEmail", string.IsNullOrWhiteSpace(options.AdminEmail) ? "admin@example.org" : options.AdminEmail),
                new XElement(Oai + "earliestDatestamp", Stamp(store.Earliest() ?? RecordStatePolicy.Stamp(time.GetUtcNow()), options)),
                new XElement(Oai + "deletedRecord", "persistent"),
                new XElement(Oai + "granularity", options.Granularity == OaiPmhGranularity.Day ? "YYYY-MM-DD" : "YYYY-MM-DDThh:mm:ssZ"));
        }

        if (verb == "ListSets")
        {
            throw new OaiException(OaiPmhErrorCode.NoSetHierarchy, "This repository does not support sets.");
        }

        if (verb == "ListMetadataFormats")
        {
            if (request.Value("identifier") is { } id && store.Find(id) is null)
            {
                throw new OaiException(OaiPmhErrorCode.IdDoesNotExist, "Unknown identifier.");
            }

            return new XElement(Oai + verb, new XElement(Oai + "metadataFormat",
                new XElement(Oai + "metadataPrefix", "oai_dc"),
                new XElement(Oai + "schema", "http://www.openarchives.org/OAI/2.0/oai_dc.xsd"),
                new XElement(Oai + "metadataNamespace", "http://www.openarchives.org/OAI/2.0/oai_dc/")));
        }
        HarvestCursor? cursor = request.Value("resumptionToken") is { } token ? tokens.Decode(token, verb) : null;
        if ((cursor?.Prefix ?? request.Value("metadataPrefix")) != "oai_dc")
        {
            throw new OaiException(OaiPmhErrorCode.CannotDisseminateFormat, "Only oai_dc is supported.");
        }

        if (verb == "GetRecord")
        {
            var record = store.Find(request.Value("identifier")!) ?? throw new OaiException(OaiPmhErrorCode.IdDoesNotExist, "Unknown identifier.");
            return new XElement(Oai + verb, Record(record, selector, options, url));
        }
        if (request.Value("set") is not null)
        {
            throw new OaiException(OaiPmhErrorCode.NoSetHierarchy, "This repository does not support sets.");
        }

        if (options.Granularity == OaiPmhGranularity.Day
            && (request.Value("from")?.Length > 10 || request.Value("until")?.Length > 10))
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Repository supports day granularity only.");
        }

        var from = cursor?.From ?? request.From;
        var until = cursor?.Until ?? request.Until;
        var page = store.Page(from, until, cursor?.After, cursor?.AfterId ?? 0, options.PageSize + 1);
        if (cursor is not null && (cursor.Version != page.Version || cursor.Total != page.Total))
        {
            throw new OaiException(OaiPmhErrorCode.BadResumptionToken, "Records changed; restart the harvest.");
        }

        if (page.Records.Count == 0)
        {
            throw new OaiException(cursor is null ? OaiPmhErrorCode.NoRecordsMatch : OaiPmhErrorCode.BadResumptionToken, "No records match.");
        }

        var result = new XElement(Oai + verb);
        long size = 4096;
        var consumed = 0;
        OaiRecord? last = null;
        foreach (var record in page.Records.Take(options.PageSize))
        {
            var element = verb == "ListIdentifiers" ? Header(Visible(record, selector), options) : Record(record, selector, options, url);
            var bytes = Encoding.UTF8.GetByteCount(element.ToString(SaveOptions.DisableFormatting));
            // Include a first oversized record: it must not silently disappear or trap the cursor.
            if (consumed > 0 && size + bytes > options.MaxPageBytes)
            {
                break;
            }

            result.Add(element);
            size += bytes;
            last = record;
            consumed++;
        }
        var offset = cursor?.Cursor ?? 0;
        var more = page.Records.Count > consumed;
        if (more || cursor is not null)
        {
            var expiry = cursor?.Expires ?? time.GetUtcNow().Add(options.TokenLifetime);
            var next = more ? tokens.Encode(new(verb, "oai_dc", from, until, last!.Datestamp, last.Id, offset + consumed,
                page.Total, page.Version, expiry, tokens.Configuration)) : "";
            var element = new XElement(Oai + "resumptionToken",
                new XAttribute("completeListSize", page.Total), new XAttribute("cursor", offset), next);
            if (more)
            {
                element.Add(new XAttribute("expirationDate", ErrorWriter.FormatResponseDate(expiry)));
            }

            result.Add(element);
        }
        return result;
    }

    private static OaiRecord Visible(OaiRecord record, IContentSelector selector) =>
        record.Deleted || selector.GetExport(record.Key) is not null ? record : record with { Deleted = true };

    private static XElement Header(OaiRecord record, OaiPmhOptions options) =>
        new(Oai + "header", record.Deleted ? new XAttribute("status", "deleted") : null,
            new XElement(Oai + "identifier", record.Identifier), new XElement(Oai + "datestamp", Stamp(record.Datestamp, options)));

    private static XElement Record(OaiRecord record, IContentSelector selector, OaiPmhOptions options, string url)
    {
        var content = record.Deleted ? null : selector.GetExport(record.Key);
        // Fail closed if current content is no longer public (even before the consistency job catches up).
        var visible = content is null ? record with { Deleted = true } : record;
        return new XElement(Oai + "record", Header(visible, options),
            content is null ? null : OaiDcWriter.Write(content, options, new Uri(url)));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "OAI-PMH {Verb} response exceeds 9.9 MB; reduce the configured export size")]
    private static partial void LogOversizedResponse(ILogger logger, string? verb);

    private static string Stamp(DateTime date, OaiPmhOptions options) => RecordStatePolicy.FormatDate(date, options.Granularity == OaiPmhGranularity.Day);
    internal static string RequestUrl(HttpRequest request, OaiPmhOptions options) =>
        options.BaseUrl?.AbsoluteUri ?? UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path);
}
