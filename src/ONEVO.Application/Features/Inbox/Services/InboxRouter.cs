using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Services;

public interface IInboxRouter
{
    Task<InboxPage> ListAsync(Guid tenantId, Guid callerEmployeeId, InboxQuery query, CancellationToken ct);
    Task<IReadOnlyList<InboxFilterField>> GetFiltersAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);
    Task<InboxCounts> GetCountsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);
    Task<Result<InboxItemDetail>> GetDetailAsync(Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, CancellationToken ct);
    Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, string action, JsonElement? body, CancellationToken ct);
}

/// <summary>
/// Sits between the Inbox API and the source modules. Fans out to the registered providers, applies the
/// filters every source shares, sorts (pending first, then most recently updated), pages, and fills the
/// unread-message count from Conversations. Providers run one after another: they share the request's
/// DbContext, which is not safe for concurrent use.
/// </summary>
public sealed class InboxRouter : IInboxRouter
{
    public const string SourceFilterKey = "source";
    public const int MaxPageSize = 100;
    private const string NotFoundMessage = "Inbox item not found.";

    private readonly IReadOnlyList<IInboxSourceProvider> _providers;
    private readonly IConversationUnreadCounts _unread;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<InboxRouter> _logger;

    public InboxRouter(
        IEnumerable<IInboxSourceProvider> providers, IConversationUnreadCounts unread, IInboxRealtimePublisher realtime,
        ILogger<InboxRouter> logger)
    {
        _providers = providers.ToList();
        _unread = unread;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<InboxPage> ListAsync(Guid tenantId, Guid callerEmployeeId, InboxQuery query, CancellationToken ct)
    {
        var items = (await CollectAsync(tenantId, callerEmployeeId, query, ct)).Where(i => Matches(i, query));
        var sorted = items.OrderByDescending(i => i.IsPending).ThenByDescending(i => i.UpdatedAt).ToList();

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);
        var pageItems = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new InboxPage(await WithUnreadAsync(tenantId, callerEmployeeId, pageItems, ct), sorted.Count, page, pageSize);
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
        return new InboxCounts(
            PendingReceived: items.Count(i => i.IsPending && i.Category == InboxCategories.Request && i.Direction == InboxDirections.Received),
            UnreadMessages: unread.Values.Sum(),
            OpenAlerts: items.Count(i => i.IsPending && i.Category == InboxCategories.Alert));
    }

    public async Task<Result<InboxItemDetail>> GetDetailAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, CancellationToken ct)
    {
        var provider = ProviderFor(source);
        var detail = provider is null ? null : await provider.GetDetailAsync(tenantId, kind, id, callerEmployeeId, ct);
        if (detail is null)
            return Result<InboxItemDetail>.NotFound(NotFoundMessage);

        var withUnread = await WithUnreadAsync(tenantId, callerEmployeeId, [detail.Item], ct);
        return Result<InboxItemDetail>.Success(detail with { Item = withUnread[0] });
    }

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, Guid callerEmployeeId, string source, string kind, Guid id, string action, JsonElement? body, CancellationToken ct)
    {
        var provider = ProviderFor(source);
        if (provider is null)
            return Result<InboxActionResult>.NotFound(NotFoundMessage);

        var result = await provider.ExecuteActionAsync(tenantId, kind, id, action, body, callerEmployeeId, ct);
        if (result.IsSuccess && result.Value!.AffectedEmployeeIds.Count > 0)
        {
            try
            {
                await _realtime.InboxChangedAsync(tenantId, result.Value.AffectedEmployeeIds, new ItemRef(provider.Source, kind, id), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Inbox realtime inboxChanged failed after {Action} on {Source}/{Kind}/{Id}", action, source, kind, id);
            }
        }
        return result;
    }

    private IInboxSourceProvider? ProviderFor(string source) => _providers.FirstOrDefault(p => p.Source == source);

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
                if (!item.Facets.TryGetValue(key, out var itemValues) || !itemValues.Intersect(values).Any())
                    return false;
            }
        }
        return true;
    }

    private static bool MatchesSearch(InboxItem item, string search)
        => new[] { item.Title, item.Subtitle, item.ContextName, item.RequestedBy?.Name, item.RequestedTo?.Name }
            .Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
}
