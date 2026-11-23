using System.Globalization;
using System.Text;
using System.Text.Json;
using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Limescape.Umbraco.OaiPmh.Records;

public interface IContentSelector { ExportContent? GetExport(Guid key); }

/// <summary>Reads published property values, never edited values. No recursive expansion of media, blocks or JSON.</summary>
public sealed class ContentSelector(IContentService contentService, IContentTypeService contentTypes, IOptions<OaiPmhOptions> options) : IContentSelector
{
    public ExportContent? GetExport(Guid key)
    {
        var content = contentService.GetById(key);
        if (content is null || !contentService.IsPathPublished(content))
        {
            return null;
        }

        return Project(content, options.Value, contentTypes.Get(content.ContentType.Id)?.CompositionAliases());
    }

    internal static ExportContent? Project(IContent content, OaiPmhOptions options, IEnumerable<string>? compositions = null)
    {
        if (!content.Published || content.Trashed || content.Blueprint
            || (options.Culture is not null && !content.PublishedCultures.Contains(options.Culture, StringComparer.OrdinalIgnoreCase))
            || !options.DocumentTypes.Intersect((compositions ?? []).Append(content.ContentType.Alias), StringComparer.OrdinalIgnoreCase).Any())
        {
            return null;
        }

        object? Published(string alias) => content.GetValue(alias, culture: options.Culture, published: true);
        IReadOnlyList<string> Values(string alias)
        {
            content.Properties.TryGetValue(alias, out var property);
            return ExportValues(Published(alias), property?.PropertyType.PropertyEditorAlias);
        }
        var sharing = string.IsNullOrEmpty(options.SharingLevelAlias) ? [] : Values(options.SharingLevelAlias);
        if (!string.IsNullOrEmpty(options.SharingLevelAlias)
            && (sharing.Count != 1 || !string.Equals(sharing[0], options.PublicSharingValue, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(options.ConfidentialAlias)
            && IsConfidential(Published(options.ConfidentialAlias)))
        {
            return null;
        }

        // Archived from public scope: any value fails closed, the record turns into a tombstone.
        if (!string.IsNullOrEmpty(options.ArchivedAlias) && Published(options.ArchivedAlias) is { } archived
            && (archived is not string text || !string.IsNullOrWhiteSpace(text)))
        {
            return null;
        }

        var fields = new List<ExportField>();
        foreach (var mapping in options.DublinCoreMapping.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var alias in mapping.Value)
            {
                if (IsExcluded(alias, options))
                {
                    continue;
                }

                foreach (var value in Values(alias))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        fields.Add(new(mapping.Key, alias, value));
                    }
                }
            }
        }
        return new(content.Key, fields, options.LandingPageUrlTemplate.Replace("{key}", content.Key.ToString("D"), StringComparison.Ordinal));
    }

    /// <summary>Non-exportable fields and the confidentiality flag itself are never exported, whatever the mapping says.</summary>
    internal static bool IsExcluded(string alias, OaiPmhOptions options) =>
        options.NonExportableFields.Contains(alias, StringComparer.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(options.ConfidentialAlias) && string.Equals(alias, options.ConfidentialAlias, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> ScalarEditors = new(StringComparer.Ordinal)
    {
        Constants.PropertyEditors.Aliases.TextBox, Constants.PropertyEditors.Aliases.TextArea,
        Constants.PropertyEditors.Aliases.Label, Constants.PropertyEditors.Aliases.Integer,
        Constants.PropertyEditors.Aliases.Decimal, Constants.PropertyEditors.Aliases.Boolean,
        Constants.PropertyEditors.Aliases.DateTime, Constants.PropertyEditors.Aliases.DateOnly,
        Constants.PropertyEditors.Aliases.DateTimeUnspecified, Constants.PropertyEditors.Aliases.DateTimeWithTimeZone
    };

    internal static IReadOnlyList<string> ExportValues(object? value, string? editorAlias)
    {
        if (editorAlias == Constants.PropertyEditors.Aliases.DropDownListFlexible && value is string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array
                    || document.RootElement.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                {
                    return [];
                }
                return document.RootElement.EnumerateArray().Select(x => x.GetString()!).ToArray();
            }
            catch (JsonException) { return []; }
        }
        if (editorAlias is not null && !ScalarEditors.Contains(editorAlias))
        {
            return []; // Even a picker stored as a plain UDI must not expose a confidential element reference.
        }
        var scalar = Scalar(value);
        return scalar is null ? [] : [scalar];
    }

    // An allow-list of primitives prevents a configured media/block picker from serializing confidential nested data.
    internal static string? Scalar(object? value) => value switch
    {
        // Structured values and Umbraco references (umb://media/..., umb://document/...) never pass: a reference names an element.
        string s when !s.TrimStart().StartsWith('{') && !s.TrimStart().StartsWith('[')
            && !s.TrimStart().StartsWith("umb://", StringComparison.OrdinalIgnoreCase) => XmlText(s),
        DateTime d => RecordStatePolicy.FormatDate(d.ToUniversalTime()),
        DateTimeOffset d => RecordStatePolicy.FormatDate(d.UtcDateTime),
        bool b => b ? "true" : "false",
        byte or short or int or long or float or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => null
    };
    internal static string XmlText(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is 9 or 10 or 13 or >= 0x20 and <= 0xd7ff or >= 0xe000 and <= 0xfffd or >= 0x10000 and <= 0x10ffff)
            {
                text.Append(rune.ToString());
            }
        }
        return text.ToString();
    }

    private static bool IsConfidential(object? value) => value is true || Scalar(value) is "true" or "1";
}
