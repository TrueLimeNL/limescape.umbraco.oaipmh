using System.Globalization;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Limescape.Umbraco.OaiPmh.Endpoint;

internal sealed class OaiException(OaiPmhErrorCode code, string message) : Exception(message)
{
    public OaiPmhErrorCode Code { get; } = code;
}

internal sealed record OaiRequest(string Verb, IReadOnlyDictionary<string, string> Arguments)
{
    public string? Value(string key) => Arguments.GetValueOrDefault(key);
    public DateTime? From => RequestParser.Date(Value("from"), false);
    public DateTime? Until => RequestParser.Date(Value("until"), true);
}

internal static class RequestParser
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Identify"] = [],
        ["ListSets"] = ["resumptionToken"],
        ["ListMetadataFormats"] = ["identifier"],
        ["GetRecord"] = ["identifier", "metadataPrefix"],
        ["ListRecords"] = ["metadataPrefix", "from", "until", "set", "resumptionToken"],
        ["ListIdentifiers"] = ["metadataPrefix", "from", "until", "set", "resumptionToken"]
    };

    public static async Task<OaiRequest> ParseAsync(HttpRequest request)
    {
        IEnumerable<KeyValuePair<string, StringValues>> source = request.Query;
        if (HttpMethods.IsPost(request.Method))
        {
            if (request.ContentType?.Split(';')[0].Trim() != "application/x-www-form-urlencoded")
            {
                throw new OaiException(OaiPmhErrorCode.BadArgument, "POST requires application/x-www-form-urlencoded.");
            }
            // OAI-PMH POST arguments are in the body only. Ambiguous query/body requests are rejected.
            if (request.Query.Count > 0)
            {
                throw new OaiException(OaiPmhErrorCode.BadArgument, "POST arguments must be in the form body.");
            }

            try { source = await request.ReadFormAsync(request.HttpContext.RequestAborted); }
            catch (InvalidDataException) { throw new OaiException(OaiPmhErrorCode.BadArgument, "Invalid form body."); }
        }
        var input = source.ToArray();
        var verbs = input.Where(x => x.Key == "verb").ToArray();
        if (verbs.Length != 1 || verbs[0].Value.Count != 1 || !Allowed.ContainsKey(verbs[0].Value.ToString()))
        {
            throw new OaiException(OaiPmhErrorCode.BadVerb, "The request does not contain a legal verb argument.");
        }

        var verb = verbs[0].Value.ToString();
        if (input.Any(x => x.Value.Count != 1 || string.IsNullOrEmpty(x.Value.ToString())
                || (x.Key != "verb" && !Allowed[verb].Contains(x.Key, StringComparer.Ordinal))))
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Unknown, empty or repeated argument.");
        }

        try
        {
            foreach (var argument in input) { XmlConvert.VerifyXmlChars(argument.Value.ToString()); }
        }
        catch (XmlException)
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Arguments must contain valid XML characters.");
        }
        var args = input.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
        if (args.ContainsKey("resumptionToken"))
        {
            if (args.Count != 2)
            {
                throw new OaiException(OaiPmhErrorCode.BadArgument, "A resumption token is exclusive.");
            }
        }
        else if ((verb == "GetRecord" && (!args.ContainsKey("identifier") || !args.ContainsKey("metadataPrefix")))
            || ((verb == "ListRecords" || verb == "ListIdentifiers") && !args.ContainsKey("metadataPrefix")))
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Missing required argument.");
        }

        var parsed = new OaiRequest(verb, args);
        var from = parsed.From;
        var until = parsed.Until;
        if (from > until || (args.ContainsKey("from") && args.ContainsKey("until") && args["from"].Length != args["until"].Length))
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Date bounds must be ordered and have the same granularity.");
        }

        return parsed;
    }

    public static DateTime? Date(string? value, bool end)
    {
        if (value is null)
        {
            return null;
        }

        var day = value.Length == 10;
        if (!DateTime.TryParseExact(value, day ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            throw new OaiException(OaiPmhErrorCode.BadArgument, "Invalid UTC datestamp.");
        }

        return day && end ? date.AddHours(23).AddMinutes(59).AddSeconds(59) : date;
    }
}
