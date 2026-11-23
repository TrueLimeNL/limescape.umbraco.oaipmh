using Microsoft.Extensions.Options;

namespace Limescape.Umbraco.OaiPmh.Configuration;

/// <summary>Rejects settings that would map the endpoint on an invalid path or advertise a relative base URL.</summary>
internal sealed class OaiPmhOptionsValidator : IValidateOptions<OaiPmhOptions>
{
    public ValidateOptionsResult Validate(string? name, OaiPmhOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.EndpointPath) || !options.EndpointPath.StartsWith('/'))
        {
            failures.Add($"{OaiPmhOptions.SectionName}:EndpointPath must start with '/'.");
        }
        else if (options.EndpointPath.Contains('?', StringComparison.Ordinal) || options.EndpointPath.Contains('#', StringComparison.Ordinal))
        {
            failures.Add($"{OaiPmhOptions.SectionName}:EndpointPath must be a path without query or fragment.");
        }

        if (options.BaseUrl is { } baseUrl
            && (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp)))
        {
            failures.Add($"{OaiPmhOptions.SectionName}:BaseUrl must be an absolute http or https URL.");
        }

        if (!Enum.IsDefined(options.Granularity))
        {
            failures.Add($"{OaiPmhOptions.SectionName}:Granularity must be Seconds or Day.");
        }

        if (options.PageSize is < 1 or > 1000)
        {
            failures.Add("PageSize must be between 1 and 1000.");
        }

        if (options.RequestsPerSecond < 1)
        {
            failures.Add("RequestsPerSecond must be positive.");
        }

        if (options.MaxPageBytes is < 8192 or > 9_500_000)
        {
            failures.Add("MaxPageBytes must be between 8192 and 9500000.");
        }

        if (options.TokenLifetime <= TimeSpan.Zero || options.TokenLifetime > TimeSpan.FromHours(24))
        {
            failures.Add("TokenLifetime must be positive and at most 24 hours.");
        }

        if (string.IsNullOrWhiteSpace(options.RepositoryIdentifier) || options.RepositoryIdentifier.Length > 200
            || options.RepositoryIdentifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
        {
            failures.Add("RepositoryIdentifier must be a stable ASCII repository name.");
        }

        if (!string.IsNullOrEmpty(options.TokenSigningKey))
        {
            try
            {
                if (Convert.FromBase64String(options.TokenSigningKey).Length < 32)
                {
                    failures.Add("TokenSigningKey must contain at least 32 bytes.");
                }
            }
            catch (FormatException) { failures.Add("TokenSigningKey must be base64."); }
        }
        if (options.ConsistencyJobTime < TimeSpan.Zero || options.ConsistencyJobTime >= TimeSpan.FromDays(1))
        {
            failures.Add("ConsistencyJobTime must be a time of day (00:00:00 to 23:59:59).");
        }

        if (options.DublinCoreMapping.Keys.Any(k => !Formats.OaiDcWriter.Elements.Contains(k)))
        {
            failures.Add("Mapping contains an unknown Dublin Core element.");
        }

        var landing = options.LandingPageUrlTemplate.Replace("{key}", Guid.Empty.ToString("D"), StringComparison.Ordinal);
        if ((!landing.StartsWith('/') || landing.StartsWith("//", StringComparison.Ordinal))
            && (!Uri.TryCreate(landing, UriKind.Absolute, out var landingUri) || landingUri.Scheme is not ("http" or "https")))
        {
            failures.Add("LandingPageUrlTemplate must be root-relative or an absolute HTTP(S) URI.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
