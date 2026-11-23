using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Extensions;

namespace Limescape.Umbraco.OaiPmh.Records;

/// <summary>Outcome of one reconciliation pass. Every correction advanced the datestamp of that record.</summary>
public sealed record ConsistencyReport(int Checked, int Created, int Updated, int Deleted, int Restored)
{
    public int Corrected => Created + Updated + Deleted + Restored;
}

/// <summary>
/// Nightly reconciliation of <c>lsOaiRecord</c> with the actual content: backfills published content that has no row
/// (first install on an existing repository), refreshes rows whose export drifted (a missed notification or a changed
/// mapping), restores rows of content that became public again, and turns rows of content that is no longer public or
/// no longer exists into tombstones. Rows are never removed. Runs on the scheduling server only.
/// </summary>
public sealed partial class ConsistencyJob(
    IRecordStateStore store,
    IContentSelector selector,
    IContentService contentService,
    IContentTypeService contentTypes,
    IOptions<OaiPmhOptions> options,
    TimeProvider time,
    ILogger<ConsistencyJob> logger) : IRecurringBackgroundJob
{
    private const int PageSize = 500;

    public TimeSpan Period => TimeSpan.FromDays(1);

    /// <summary>Time until the next configured UTC time of day, so the pass runs at night whatever the start time.</summary>
    public TimeSpan Delay
    {
        get
        {
            var now = time.GetUtcNow();
            var next = now.UtcDateTime.Date + options.Value.ConsistencyJobTime;
            return next > now.UtcDateTime ? next - now.UtcDateTime : next.AddDays(1) - now.UtcDateTime;
        }
    }

    public ServerRole[] ServerRoles => [ServerRole.Single, ServerRole.SchedulingPublisher];

    public event EventHandler PeriodChanged { add { } remove { } }

    public Task RunJobAsync()
    {
        if (options.Value.Enabled && options.Value.ConsistencyJobEnabled)
        {
            Run(CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <summary>Runs one pass over all content of the configured types and all active rows.</summary>
    public ConsistencyReport Run(CancellationToken cancellationToken)
    {
        var configured = options.Value.DocumentTypes;
        var typeIds = contentTypes.GetAll()
            .Where(t => configured.Intersect(t.CompositionAliases().Append(t.Alias), StringComparer.OrdinalIgnoreCase).Any())
            .Select(t => t.Id)
            .ToArray();
        var seen = new HashSet<Guid>();
        var counts = new Dictionary<RecordChange, int>();

        void Check(Guid key)
        {
            var change = store.Update(key, selector.GetExport(key));
            if (change != RecordChange.None)
            {
                counts[change] = counts.GetValueOrDefault(change) + 1;
                LogCorrected(logger, key, change);
            }
        }

        for (long page = 0; typeIds.Length > 0 && !cancellationToken.IsCancellationRequested; page++)
        {
            var batch = contentService.GetPagedOfTypes(typeIds, page, PageSize, out var total, null, null).ToArray();
            foreach (var content in batch)
            {
                if (seen.Add(content.Key))
                {
                    Check(content.Key);
                }
            }

            if (batch.Length < PageSize || (page + 1) * PageSize >= total)
            {
                break;
            }
        }

        // Rows of purged content or of types that left the configured scope.
        foreach (var key in store.ActiveKeys())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (seen.Add(key))
            {
                Check(key);
            }
        }

        var report = new ConsistencyReport(seen.Count, counts.GetValueOrDefault(RecordChange.Created), counts.GetValueOrDefault(RecordChange.Updated),
            counts.GetValueOrDefault(RecordChange.Deleted), counts.GetValueOrDefault(RecordChange.Restored));
        LogCompleted(logger, report.Checked, report.Corrected, report.Created, report.Updated, report.Deleted, report.Restored);
        return report;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAI-PMH record of document {Key} was out of sync with the content; corrected ({Change})")]
    private static partial void LogCorrected(ILogger logger, Guid key, RecordChange change);

    [LoggerMessage(Level = LogLevel.Information, Message = "OAI-PMH consistency check: {Checked} document(s) checked, {Corrected} corrected (created {Created}, updated {Updated}, deleted {Deleted}, restored {Restored})")]
    private static partial void LogCompleted(ILogger logger, int @checked, int corrected, int created, int updated, int deleted, int restored);
}
