using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;

namespace ONEVO.Application.Features.Inbox.Contracts;

/// <summary>Action keys the router itself understands for every kind.</summary>
public static class InboxStandardActions
{
    public const string Revert = "revert";
    public const string MetaRevertableUntil = "revertableUntil";
}

/// <summary>One row of the History tab: a decision the caller took. <see cref="Item"/> is null when the caller
/// can no longer see the item; <see cref="Title"/> then comes from the journal snapshot.</summary>
public sealed record InboxHistoryEntry(
    Guid JournalId, string Source, string Kind, Guid ItemId, string Action, string ActionLabel, DateTimeOffset DecidedAt,
    string? Comment, bool IsReverted, bool CanRevert, DateTimeOffset? RevertableUntil, string Title, InboxItem? Item);

public sealed record InboxHistoryPage(IReadOnlyList<InboxHistoryEntry> Items, int Total, int Page, int PageSize);

public sealed record InboxHistoryQuery(
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Source = null, string? Kind = null, int Page = 1, int PageSize = 25);

/// <summary>POST /inbox/bulk. <see cref="Outcome"/> is for "resolve".</summary>
public sealed record InboxBulkRequest(string Action, IReadOnlyList<ItemRef> Items, string? Comment = null, string? Outcome = null);

public sealed record InboxBulkFailure(ItemRef Ref, string Message);

public sealed record InboxBulkResult(IReadOnlyList<ItemRef> Succeeded, IReadOnlyList<InboxBulkFailure> Failed);

/// <summary>A provider that can run one action over many of its items in a single module call (Leave groups
/// into its existing bulk approve/reject commands). Providers without it get one action per item.</summary>
public interface IInboxBulkActionProvider
{
    /// <summary>Null = this provider does not batch this (kind, action); the router falls back to per-item.</summary>
    Task<IReadOnlyList<(ItemRef Ref, Result<InboxActionResult> Result)>?> ExecuteBulkAsync(
        Guid tenantId, string kind, string action, IReadOnlyList<Guid> ids, JsonElement body, Guid callerEmployeeId, CancellationToken ct);
}
