using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Limescape.Umbraco.OaiPmh.Tokens;

internal sealed record HarvestCursor(string Verb, string Prefix, DateTime? From, DateTime? Until,
    DateTime After, long AfterId, long Cursor, long Total, string Version, DateTimeOffset Expires, string Configuration);

/// <summary>Stateless HMAC-SHA256 cursor; the same configured key must be used by every replica.</summary>
public sealed class ResumptionTokenService
{
    private readonly byte[] _key;
    private readonly TimeProvider _time;
    internal string Configuration { get; }
    public ResumptionTokenService(IOptions<OaiPmhOptions> options, TimeProvider time)
    {
        _time = time;
        var settings = options.Value;
        _key = string.IsNullOrEmpty(settings.TokenSigningKey) ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(settings.TokenSigningKey);
        // A mapping, exclusion, repository or page size change requires a fresh harvest.
        Configuration = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))));
    }

    internal string Encode(HarvestCursor cursor)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return WebEncoders.Base64UrlEncode(payload) + "." + WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, payload));
    }

    internal HarvestCursor Decode(string token, string verb)
    {
        try
        {
            if (token.Length > 8192)
            {
                throw new FormatException();
            }

            var parts = token.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload)))
            {
                throw new FormatException();
            }

            var cursor = JsonSerializer.Deserialize<HarvestCursor>(payload) ?? throw new FormatException();
            if (cursor.Verb != verb || cursor.Prefix != "oai_dc" || cursor.Expires <= _time.GetUtcNow()
                || cursor.Configuration != Configuration || cursor.Cursor < 0 || cursor.AfterId <= 0)
            {
                throw new FormatException();
            }

            return cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new Endpoint.OaiException(Endpoint.OaiPmhErrorCode.BadResumptionToken, "The resumption token is invalid, expired or belongs to another request.");
        }
    }
}
