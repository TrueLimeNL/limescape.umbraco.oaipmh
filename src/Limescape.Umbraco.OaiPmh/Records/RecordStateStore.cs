using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.Extensions.Options;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Limescape.Umbraco.OaiPmh.Records;

[TableName("lsOaiRecord"), PrimaryKey("id", AutoIncrement = true), ExplicitColumns]
internal sealed class RecordDto
{
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public long Id { get; set; }
    [Column("documentKey"), Index(IndexTypes.UniqueNonClustered, Name = "IX_lsOaiRecord_key")] public Guid Key { get; set; }
    [Column("identifier"), Length(255), Index(IndexTypes.UniqueNonClustered, Name = "IX_lsOaiRecord_identifier")] public string Identifier { get; set; } = "";
    [Column("datestamp"), Index(IndexTypes.NonClustered, Name = "IX_lsOaiRecord_stamp")] public DateTime Datestamp { get; set; }
    [Column("deleted")] public bool Deleted { get; set; }
    [Column("exportHash"), Length(64)] public string ExportHash { get; set; } = "";
    [Column("revision")] public long Revision { get; set; }
    public OaiRecord Record() => new(Id, Key, Identifier, DateTime.SpecifyKind(Datestamp, DateTimeKind.Utc), Deleted, ExportHash, Revision);
}

/// <summary>Independent durable table; rows are never removed, including on content purge.</summary>
public sealed class RecordStateStore(IScopeProvider scopes, IOptions<OaiPmhOptions> options, TimeProvider time) : IRecordStateStore
{
    public OaiRecord? Find(string identifier)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        return scope.Database.SingleOrDefault<RecordDto>("WHERE identifier=@0", identifier)?.Record();
    }

    public DateTime? Earliest()
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<DateTime?>("SELECT MIN(datestamp) FROM lsOaiRecord");
    }

    public RecordPage Page(DateTime? from, DateTime? until, DateTime? after, long afterId, int take)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var filter = new Sql("WHERE 1=1");
        if (from is not null)
        {
            filter.Append("AND datestamp>=@0", from.Value);
        }

        if (until is not null)
        {
            filter.Append("AND datestamp<=@0", until.Value);
        }

        var total = scope.Database.ExecuteScalar<long>(new Sql("SELECT COUNT(*) FROM lsOaiRecord").Append(filter));
        // Revision sum changes even for updates within the same second, deletions and restores. Global invalidation is deliberate.
        var version = scope.Database.ExecuteScalar<long>("SELECT COALESCE(SUM(revision),0) FROM lsOaiRecord").ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (after is not null)
        {
            filter.Append("AND (datestamp>@0 OR (datestamp=@0 AND id>@1))", after.Value, afterId);
        }

        filter.Append("ORDER BY datestamp,id");
        var page = scope.Database.SkipTake<RecordDto>(0, take, filter).Select(x => x.Record()).ToArray();
        return new(page, total, version);
    }

    public IReadOnlyCollection<Guid> ActiveKeys()
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        return scope.Database.Fetch<Guid>("SELECT documentKey FROM lsOaiRecord WHERE deleted=@0", false);
    }

    public RecordChange Update(Guid key, ExportContent? content)
    {
        using var scope = scopes.CreateScope();
        var row = scope.Database.SingleOrDefault<RecordDto>("WHERE documentKey=@0", key);
        if (row is null && content is null) { scope.Complete(); return RecordChange.None; } // Never-published draft: no identifier.
        var hash = content is null ? "" : RecordStatePolicy.Hash(content);
        var deleted = content is null;
        if (row is not null && row.Deleted == deleted && row.ExportHash == hash) { scope.Complete(); return RecordChange.None; }
        RecordChange change;
        if (row is null)
        {
            row = new()
            {
                Key = key,
                Identifier = RecordStatePolicy.Identifier(options.Value.RepositoryIdentifier, key),
                Datestamp = RecordStatePolicy.Stamp(time.GetUtcNow()),
                ExportHash = hash,
                Revision = 1
            };
            scope.Database.Insert(row);
            change = RecordChange.Created;
        }
        else
        {
            change = deleted ? RecordChange.Deleted : row.Deleted ? RecordChange.Restored : RecordChange.Updated;
            row.Datestamp = RecordStatePolicy.Stamp(time.GetUtcNow());
            row.Deleted = deleted;
            row.ExportHash = hash;
            row.Revision++;
            scope.Database.Update(row);
        }
        scope.Complete();
        return change;
    }
}
