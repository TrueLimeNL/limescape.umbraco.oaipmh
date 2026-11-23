using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Limescape.Umbraco.OaiPmh.Records;

/// <summary>
/// Notifications update only exported published metadata. A restore/move also rechecks public visibility.
/// Umbraco raises unpublish, move and publish for the subtree root only, while the visibility of every published
/// descendant follows its ancestors; descendants are rechecked too, so a tombstone appears at the moment of the change.
/// </summary>
public sealed class RecordStateHandlers(IRecordStateStore store, IContentSelector selector, IContentService contentService)
    : INotificationHandler<ContentPublishedNotification>, INotificationHandler<ContentUnpublishedNotification>,
      INotificationHandler<ContentMovedToRecycleBinNotification>, INotificationHandler<ContentDeletedNotification>,
      INotificationHandler<ContentMovedNotification>
{
    private const int DescendantPageSize = 500;

    public void Handle(ContentPublishedNotification notification) => Refresh(notification.PublishedEntities.SelectMany(WithDescendants));
    public void Handle(ContentUnpublishedNotification notification)
    {
        Forget(notification.UnpublishedEntities);
        Refresh(notification.UnpublishedEntities.SelectMany(Descendants));
    }
    public void Handle(ContentDeletedNotification notification) => Forget(notification.DeletedEntities);
    public void Handle(ContentMovedToRecycleBinNotification notification) => Forget(notification.MoveInfoCollection.Select(x => x.Entity).SelectMany(WithDescendants));
    public void Handle(ContentMovedNotification notification) => Refresh(notification.MoveInfoCollection.Select(x => x.Entity).SelectMany(WithDescendants));

    private void Refresh(IEnumerable<IContent> items)
    {
        foreach (var item in items.DistinctBy(x => x.Key))
        {
            store.Update(item.Key, selector.GetExport(item.Key));
        }
    }

    private void Forget(IEnumerable<IContent> items)
    {
        foreach (var item in items.DistinctBy(x => x.Key))
        {
            store.Update(item.Key, null);
        }
    }

    private IEnumerable<IContent> WithDescendants(IContent item) => Descendants(item).Prepend(item);

    private IEnumerable<IContent> Descendants(IContent item)
    {
        if (item.Id <= 0)
        {
            yield break;
        }

        for (long page = 0; ; page++)
        {
            var batch = contentService.GetPagedDescendants(item.Id, page, DescendantPageSize, out var total).ToArray();
            foreach (var descendant in batch)
            {
                yield return descendant;
            }

            if (batch.Length < DescendantPageSize || (page + 1) * DescendantPageSize >= total)
            {
                yield break;
            }
        }
    }
}
