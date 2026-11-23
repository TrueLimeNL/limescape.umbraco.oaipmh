using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Limescape.Umbraco.OaiPmh.Records;

/// <summary>Only scalar, explicitly mapped published values are captured. Aliases remain available for exclusion checks.</summary>
public sealed record ExportField(string Element, string Alias, string Value);
public sealed record ExportContent(Guid Key, IReadOnlyList<ExportField> Fields, string LandingPage);
public sealed record OaiRecord(long Id, Guid Key, string Identifier, DateTime Datestamp, bool Deleted, string ExportHash, long Revision);
public sealed record RecordPage(IReadOnlyList<OaiRecord> Records, long Total, string Version);

/// <summary>What <see cref="IRecordStateStore.Update"/> changed. Anything but None advances the datestamp.</summary>
public enum RecordChange { None, Created, Updated, Deleted, Restored }

public interface IRecordStateStore
{
    OaiRecord? Find(string identifier);
    DateTime? Earliest();
    RecordPage Page(DateTime? from, DateTime? until, DateTime? after, long afterId, int take);
    /// <summary>Records the current export of a document; null means it is not public (anymore) and leaves a tombstone.</summary>
    RecordChange Update(Guid key, ExportContent? content);
    /// <summary>Document keys of all rows that are not tombstones, for reconciliation.</summary>
    IReadOnlyCollection<Guid> ActiveKeys();
}

/// <summary>Shared state transition policy: internal saves and repeated deletes never change datestamps.</summary>
public static class RecordStatePolicy
{
    public static string Hash(ExportContent content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Fields = content.Fields.Select(f => new { f.Element, Value = ContentSelector.XmlText(f.Value) }), content.LandingPage }))));
    public static DateTime Stamp(DateTimeOffset now) =>
        new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    public static string Identifier(string repository, Guid key) => $"oai:{repository}:{key:D}";
    public static string FormatDate(DateTime value, bool day = false) =>
        value.ToString(day ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
