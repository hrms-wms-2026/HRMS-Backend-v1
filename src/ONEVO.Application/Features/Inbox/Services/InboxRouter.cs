using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Services;

public interface IInboxRouter
{
    Task<InboxPage> ListAsync(Guid tenantId, Guid callerEmployeeId, InboxQuery query, CancellationToken ct);
    Task<IReadOnlyList<InboxFilterField>> GetFiltersAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);
    Task<InboxCounts> GetCountsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);
    Task<Result<InboxItemDetail>> GetDetailAsync(Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, CancellationToken ct);
    Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, string action, JsonElement? body, CancellationToken ct);
    IReadOnlyList<InboxKindDescriptor> GetKinds();
    Task<InboxHistoryPage> GetHistoryAsync(Guid tenantId, Guid callerEmployeeId, InboxHistoryQuery query, CancellationToken ct);
    Task<Result<InboxBulkResult>> ExecuteBulkAsync(Guid tenantId, Guid callerEmployeeId, InboxBulkRequest request, CancellationToken ct);
}

/// <summary>
/// Sits between the Inbox API and the source modules. Fans out to the registered providers, applies the
/// filters every source shares, sorts (pending first, then most recently updated), pages, and fills the
/// unread-message count from Conversations. Providers run one after another: they share the request's
/// DbContext, which is not safe for concurrent use.
/// The Inbox Standard parts every kind shares live here too: the kind registry, the generic "revert" action
/// (run by the kind's <see cref="IInboxDecisionReverter"/>), one merged Steps timeline, History and Bulk.
/// </summary>
public sealed class InboxRouter : IInboxRouter
{
    public const string SourceFilterKey = "source";
    public const string KindFilterKey = "kind";
    public const int MaxPageSize = 100;
    public const int MaxBulkItems = 100;
    private const string NotFoundMessage = "Inbox item not found.";
    public const string BulkNotAllowedMessage = "Bulk actions are not available for one or more of the selected items.";
    public const string BulkEmptyMessage = "Select at least one item.";

    private readonly IReadOnlyList<IInboxSourceProvider> _providers;
    private readonly IConversationUnreadCounts _unread;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<InboxRouter> _logger;
    private readonly IInboxKindRegistry _kinds;
    private readonly IInboxDecisionJournal _journal;
    private readonly IReadOnlyList<IInboxDecisionReverter> _reverters;
    private readonly IInboxStepsBuilder _steps;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public InboxRouter(
        IEnumerable<IInboxSourceProvider> providers, IConversationUnreadCounts unread, IInboxRealtimePublisher realtime,
        ILogger<InboxRouter> logger, IInboxKindRegistry kinds, IInboxDecisionJournal journal,
        IEnumerable<IInboxDecisionReverter> reverters, IInboxStepsBuilder steps, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _providers = providers.ToList();
        _unread = unread;
        _realtime = realtime;
        _logger = logger;
        _kinds = kinds;
        _journal = journal;
        _reverters = reverters.ToList();
        _steps = steps;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public IReadOnlyList<InboxKindDescriptor> GetKinds() => _kinds.All;

    public async Task<InboxPage> ListAsync(Guid tenantId, Guid callerEmployeeId, InboxQuery query, CancellationToken ct)
    {
        var items = (await CollectAsync(tenantId, callerEmployeeId, query, ct)).Where(i => Matches(i, query));
        var sorted = items.OrderByDescending(i => i.IsPending).ThenByDescending(i => i.UpdatedAt).ToList();

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);
        var pageItems = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var withRevert = await WithRevertActionAsync(tenantId, callerEmployeeId, pageItems, ct);
        return new InboxPage(await WithUnreadAsync(tenantId, callerEmployeeId, withRevert, ct), sorted.Count, page, pageSize);
    }

    public async Task<IReadOnlyList<InboxFilterField>> GetFiltersAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var fields = new List<InboxFilterField>
        {
            new(SourceFilterKey, "Source", Multi: true, _providers.Select(p => new InboxFilterOption(p.Source, p.Label)).ToList())
        };
        foreach (var provider in _providers)
        {
            foreach (var field in await provider.GetFilterFieldsAsync(tenantId, callerEmployeeId, ct))
            {
                var index = fields.FindIndex(f => f.Key == field.Key);
                if (index < 0)
                {
                    fields.Add(field);
                    continue;
                }
                var merged = fields[index].Options.Concat(field.Options).DistinctBy(o => o.Value).ToList();
                fields[index] = fields[index] with { Options = merged };
            }
        }
        return fields;
    }

    public async Task<InboxCounts> GetCountsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await CollectAsync(tenantId, callerEmployeeId, new InboxQuery(), ct);
        var unread = await _unread.GetAsync(tenantId, callerEmployeeId, items.Select(Ref).ToList(), ct);
        var needsMe = items.Where(i => i.IsPending
            && (i.Category == InboxCategories.Alert || (i.Category == InboxCategories.Request && i.Direction == InboxDirections.Received))).ToList();
        return new InboxCounts(
            PendingReceived: items.Count(i => i.IsPending && i.Category == InboxCategories.Request && i.Direction == InboxDirections.Received),
            UnreadMessages: unread.Values.Sum(),
            OpenAlerts: items.Count(i => i.IsPending && i.Category == InboxCategories.Alert),
            BySource: needsMe.GroupBy(i => i.Source).ToDictionary(g => g.Key, g => g.Count()),
            ByKind: needsMe.GroupBy(i => $"{i.Source}:{i.Kind}").ToDictionary(g => g.Key, g => g.Count()));
    }

    public async Task<Result<InboxItemDetail>> GetDetailAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, CancellationToken ct)
    {
        var provider = ProviderFor(source);
        var detail = provider is null ? null : await provider.GetDetailAsync(tenantId, kind, id, callerEmployeeId, ct);
        if (detail is null)
            return Result<InboxItemDetail>.NotFound(NotFoundMessage);

        var steps = await _steps.BuildAsync(tenantId, new ItemRef(provider!.Source, kind, id), detail.Steps, ct);
        var item = (await WithRevertActionAsync(tenantId, callerEmployeeId, [detail.Item], ct, checkDependencies: true))[0];
        var withUnread = await WithUnreadAsync(tenantId, callerEmployeeId, [item], ct);
        return Result<InboxItemDetail>.Success(detail with { Item = withUnread[0], Steps = steps });
    }

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, string action, JsonElement? body, CancellationToken ct)
    {
        var provider = ProviderFor(source);
        if (provider is null)
            return Result<InboxActionResult>.NotFound(NotFoundMessage);

        var reverter = action == InboxStandardActions.Revert ? ReverterFor(source, kind) : null;
        var result = reverter is not null
            ? await reverter.RevertAsync(tenantId, new ItemRef(source, kind, id), callerEmployeeId, BodyString(body, "comment"), ct)
            : await provider.ExecuteActionAsync(tenantId, kind, id, action, body, callerEmployeeId, ct);
        await PublishAsync(tenantId, result, new ItemRef(provider.Source, kind, id), action, ct);
        return result;
    }

    public async Task<InboxHistoryPage> GetHistoryAsync(Guid tenantId, Guid callerEmployeeId, InboxHistoryQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);
        var journal = await _journal.ListByActorAsync(
            tenantId, callerEmployeeId, query.From, query.To, query.Source, query.Kind, page, pageSize, ct);

        var items = new Dictionary<ItemRef, InboxItem?>();
        var latest = new Dictionary<ItemRef, InboxDecisionJournalEntry?>();
        var rows = new List<InboxHistoryEntry>();
        foreach (var entry in journal.Entries)
        {
            var itemRef = new ItemRef(entry.Source, entry.Kind, entry.ItemId);
            if (!items.TryGetValue(itemRef, out var item))
            {
                var provider = ProviderFor(entry.Source);
                item = provider is null ? null : (await provider.GetDetailAsync(tenantId, entry.Kind, entry.ItemId, callerEmployeeId, ct))?.Item;
                items[itemRef] = item;
            }
            var (canRevert, until) = InboxRevertWindow.Compute(entry, callerEmployeeId, _clock.UtcNow);
            if (canRevert && ReverterFor(entry.Source, entry.Kind) is not null)
            {
                if (!latest.TryGetValue(itemRef, out var newest))
                    latest[itemRef] = newest = await _journal.LatestAsync(tenantId, itemRef, ct);
                canRevert = newest?.Id == entry.Id;
            }
            else
            {
                canRevert = false;
            }
            rows.Add(new InboxHistoryEntry(
                entry.Id, entry.Source, entry.Kind, entry.ItemId, entry.Action, InboxStepsBuilder.JournalLabel(entry.Action),
                entry.DecidedAt, entry.Comment, entry.RevertedByJournalId is not null, canRevert, until,
                item?.Title ?? SnapshotTitle(entry) ?? "Item no longer available", item));
        }
        return new InboxHistoryPage(rows, journal.Total, page, pageSize);
    }

    public async Task<Result<InboxBulkResult>> ExecuteBulkAsync(
        Guid tenantId, Guid callerEmployeeId, InboxBulkRequest request, CancellationToken ct)
    {
        var refs = request.Items.Distinct().ToList();
        if (refs.Count == 0)
            return Result<InboxBulkResult>.UnprocessableEntity(BulkEmptyMessage);
        if (refs.Count > MaxBulkItems)
            return Result<InboxBulkResult>.UnprocessableEntity($"At most {MaxBulkItems} items can be updated at once.");
        if (refs.Any(r => _kinds.Find(r.Source, r.Kind)?.Has(InboxCapabilities.Bulk) != true || ProviderFor(r.Source) is null))
            return Result<InboxBulkResult>.UnprocessableEntity(BulkNotAllowedMessage);

        var body = JsonSerializer.SerializeToElement(new { comment = request.Comment, outcome = request.Outcome });
        var succeeded = new List<ItemRef>();
        var failed = new List<InboxBulkFailure>();

        void Collect(ItemRef itemRef, Result<InboxActionResult> result)
        {
            if (result.IsSuccess)
                succeeded.Add(itemRef);
            else
                failed.Add(new InboxBulkFailure(itemRef, result.Error ?? "The action failed."));
        }

        foreach (var group in refs.GroupBy(r => (r.Source, r.Kind)))
        {
            var provider = ProviderFor(group.Key.Source)!;
            if (provider is IInboxBulkActionProvider batching)
            {
                var batched = await batching.ExecuteBulkAsync(
                    tenantId, group.Key.Kind, request.Action, group.Select(r => r.Id).ToList(), body, callerEmployeeId, ct);
                if (batched is not null)
                {
                    foreach (var (itemRef, result) in batched)
                    {
                        Collect(itemRef, result);
                        await PublishAsync(tenantId, result, itemRef, request.Action, ct);
                    }
                    _unitOfWork.ClearTracking();
                    continue;
                }
            }
            foreach (var itemRef in group)
            {
                Result<InboxActionResult> result;
                try
                {
                    result = await provider.ExecuteActionAsync(tenantId, itemRef.Kind, itemRef.Id, request.Action, body, callerEmployeeId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Inbox bulk {Action} failed on {Source}/{Kind}/{Id}", request.Action, itemRef.Source, itemRef.Kind, itemRef.Id);
                    result = Result<InboxActionResult>.Failure("The action failed.", 500);
                }
                // A failed item's tracked-but-unsaved changes must never be flushed by the next item's save.
                _unitOfWork.ClearTracking();
                Collect(itemRef, result);
                await PublishAsync(tenantId, result, itemRef, request.Action, ct);
            }
        }
        return Result<InboxBulkResult>.Success(new InboxBulkResult(succeeded, failed));
    }

    private IInboxSourceProvider? ProviderFor(string source) => _providers.FirstOrDefault(p => p.Source == source);

    /// <summary>The kind's reverter, only when its descriptor declares Revert. Kinds without one keep handling
    /// "revert" in their provider (Work Management until it is aligned).</summary>
    private IInboxDecisionReverter? ReverterFor(string source, string kind)
        => _kinds.Find(source, kind)?.Has(InboxCapabilities.Revert) == true
            ? _reverters.FirstOrDefault(r => r.Source == source && r.Kind == kind)
            : null;

    private async Task PublishAsync(Guid tenantId, Result<InboxActionResult> result, ItemRef itemRef, string action, CancellationToken ct)
    {
        if (!result.IsSuccess || result.Value!.AffectedEmployeeIds.Count == 0)
            return;
        try
        {
            await _realtime.InboxChangedAsync(tenantId, result.Value.AffectedEmployeeIds, itemRef, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox realtime inboxChanged failed after {Action} on {Source}/{Kind}/{Id}",
                action, itemRef.Source, itemRef.Kind, itemRef.Id);
        }
    }

    /// <summary>Adds the generic "revert" action (with Meta.revertableUntil) to items the caller decided within the
    /// window, when the kind has a reverter and that decision is still the item's latest. One journal query for the
    /// caller's recent decisions, then one latest-lookup per candidate (only items decided in the last 30 minutes).</summary>
    private async Task<IReadOnlyList<InboxItem>> WithRevertActionAsync(
        Guid tenantId, Guid callerEmployeeId, IReadOnlyList<InboxItem> items, CancellationToken ct, bool checkDependencies = false)
    {
        var candidates = items.Where(i => ReverterFor(i.Source, i.Kind) is not null
            && i.AvailableActions.All(a => a.Key != InboxStandardActions.Revert)).ToList();
        if (candidates.Count == 0)
            return items;

        var now = _clock.UtcNow;
        var recent = await _journal.ListByActorAsync(
            tenantId, callerEmployeeId, now.AddMinutes(-InboxRevertWindow.Minutes), now, null, null, 1, 500, ct);
        if (recent.Entries.Count == 0)
            return items;

        var revertable = new Dictionary<ItemRef, DateTimeOffset>();
        foreach (var item in candidates)
        {
            var itemRef = Ref(item);
            var mine = recent.Entries.FirstOrDefault(e => e.Source == item.Source && e.Kind == item.Kind && e.ItemId == item.Id);
            if (mine is null)
                continue;
            var latest = await _journal.LatestAsync(tenantId, itemRef, ct);
            if (latest is null || latest.Id != mine.Id)
                continue;
            var (canRevert, until) = InboxRevertWindow.Compute(latest, callerEmployeeId, now);
            if (!canRevert)
                continue;
            if (checkDependencies && !(await ReverterFor(item.Source, item.Kind)!.CanRevertAsync(tenantId, itemRef, callerEmployeeId, ct)).IsSuccess)
                continue;
            revertable[itemRef] = until!.Value;
        }
        return items.Select(i => revertable.TryGetValue(Ref(i), out var until)
            ? i with
            {
                AvailableActions = i.AvailableActions.Append(new InboxAction(
                    InboxStandardActions.Revert, "Undo", "neutral", NeedsComment: false, NeedsPayload: false,
                    new Dictionary<string, string> { [InboxStandardActions.MetaRevertableUntil] = until.ToString("O") })).ToList(),
            }
            : i).ToList();
    }

    private async Task<List<InboxItem>> CollectAsync(Guid tenantId, Guid callerEmployeeId, InboxQuery query, CancellationToken ct)
    {
        var sources = query.Source is { Count: > 0 } wanted ? _providers.Where(p => wanted.Contains(p.Source)) : _providers;
        var items = new List<InboxItem>();
        foreach (var provider in sources)
            items.AddRange(await provider.ListAsync(tenantId, query, callerEmployeeId, ct));
        return items;
    }

    private async Task<IReadOnlyList<InboxItem>> WithUnreadAsync(Guid tenantId, Guid callerEmployeeId, IReadOnlyList<InboxItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return items;
        var unread = await _unread.GetAsync(tenantId, callerEmployeeId, items.Select(Ref).ToList(), ct);
        return items.Select(i => i with { UnreadMessages = unread.GetValueOrDefault(Ref(i)) }).ToList();
    }

    private static ItemRef Ref(InboxItem item) => new(item.Source, item.Kind, item.Id);

    private static string? BodyString(JsonElement? body, string property)
        => body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The "title" of the journal's after/before snapshot, for items the caller can no longer open.</summary>
    private static string? SnapshotTitle(InboxDecisionJournalEntry entry)
    {
        foreach (var json in new[] { entry.AfterJson, entry.BeforeJson })
        {
            if (string.IsNullOrWhiteSpace(json))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("title", out var title)
                    && title.ValueKind == JsonValueKind.String)
                    return title.GetString();
            }
            catch (JsonException)
            {
                // A snapshot without a usable title falls through to the generic label.
            }
        }
        return null;
    }

    private static bool Matches(InboxItem item, InboxQuery q)
    {
        static bool Any<T>(IReadOnlyList<T>? wanted, T value) => wanted is not { Count: > 0 } || wanted.Contains(value);

        if (!Any(q.Category, item.Category) || !Any(q.Status, item.Status) || !Any(q.Direction, item.Direction)
            || !Any(q.ActionTypes, item.ActionType))
            return false;
        if (q.ContextIds is { Count: > 0 } && (item.ContextId is not { } contextId || !q.ContextIds.Contains(contextId)))
            return false;
        if (q.RequestedBy is { Count: > 0 } && (item.RequestedBy is null || !q.RequestedBy.Contains(item.RequestedBy.Id)))
            return false;
        if (q.From is { } from && item.CreatedAt < from)
            return false;
        if (q.To is { } to && item.CreatedAt > to)
            return false;
        if (!string.IsNullOrWhiteSpace(q.Search) && !MatchesSearch(item, q.Search.Trim()))
            return false;
        if (q.Facets is { Count: > 0 })
        {
            foreach (var (key, values) in q.Facets)
            {
                if (values.Count == 0)
                    continue;
                // "kind" filters by the item's own kind; a provider need not repeat it as a facet.
                IReadOnlyList<string>? itemValues = item.Facets.TryGetValue(key, out var facetValues) ? facetValues
                    : key == KindFilterKey ? [item.Kind] : null;
                if (itemValues is null || !itemValues.Intersect(values).Any())
                    return false;
            }
        }
        return true;
    }

    private static bool MatchesSearch(InboxItem item, string search)
        => new[] { item.Title, item.Subtitle, item.ContextName, item.RequestedBy?.Name, item.RequestedTo?.Name }
            .Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
}
