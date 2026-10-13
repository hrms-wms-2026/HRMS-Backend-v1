using System.Text.Json;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Inbox.Contracts;

public static class InboxCategories
{
    public const string Request = "request";
    public const string Alert = "alert";
}

public static class InboxDirections
{
    public const string Received = "received";
    public const string Sent = "sent";
    /// <summary>Neither sent nor addressed to the caller for a decision (alerts, watched items).</summary>
    public const string Info = "info";
}

public sealed record InboxPerson(Guid Id, string Name);

/// <summary>An action the caller may run on the item right now. Tone drives the button style
/// ("primary" | "danger" | "neutral").</summary>
public sealed record InboxAction(string Key, string Label, string Tone, bool NeedsComment, bool NeedsPayload);

/// <summary>One row of the inbox, the same shape for every source module. <see cref="Facets"/> carries
/// source-specific filterable values (e.g. "module" → module ids) matched by the router generically.</summary>
public sealed record InboxItem(
    string Source, string Kind, Guid Id, string Category, string ActionType, string Title, string? Subtitle,
    string Status, string Direction, string? Severity,
    string ContextType, Guid? ContextId, string? ContextName,
    InboxPerson? RequestedBy, InboxPerson? RequestedTo,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<InboxAction> AvailableActions, int UnreadMessages, bool IsPending,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Facets);

public sealed record InboxStep(string Key, string Label, InboxPerson? Actor, DateTimeOffset? At, string State, string? Note);

public static class InboxStepStates
{
    public const string Done = "done";
    public const string Current = "current";
    public const string Upcoming = "upcoming";
}

/// <summary>One row of the change set. Kind ("text", "date", "hours", "employee", …) and Editable let the
/// approver adjust the requested value before approving (WM "edit before approve").</summary>
public sealed record InboxChange(
    string Field, string Label, string? Before, string? After, string Kind = "text", bool Editable = false, string? Applied = null);

/// <summary>Where "open" goes in the frontend: router commands plus optional query params.</summary>
public sealed record InboxTarget(IReadOnlyList<string> Route, string Label, IReadOnlyDictionary<string, string>? QueryParams = null);

public sealed record InboxItemDetail(
    InboxItem Item, IReadOnlyList<InboxStep> Steps, string? Summary, IReadOnlyList<InboxChange> ChangeSet,
    string? Explanation, IReadOnlyDictionary<string, string>? ExplanationDetails, InboxTarget? Target,
    string? EditablePayloadJson);

public sealed record InboxFilterOption(string Value, string Label);

/// <summary>A filter the inbox page renders. Fields with the same key from several providers are merged.</summary>
public sealed record InboxFilterField(string Key, string Label, bool Multi, IReadOnlyList<InboxFilterOption> Options);

public sealed record InboxQuery(
    IReadOnlyList<string>? Category = null, IReadOnlyList<string>? Source = null, IReadOnlyList<Guid>? ContextIds = null,
    IReadOnlyList<string>? Status = null, IReadOnlyList<string>? Direction = null, IReadOnlyList<string>? ActionTypes = null,
    IReadOnlyList<Guid>? RequestedBy = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Search = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Facets = null, int Page = 1, int PageSize = 25);

/// <summary>Who must refresh their inbox after an action (requester, approvers, …).</summary>
public sealed record InboxActionResult(IReadOnlyCollection<Guid> AffectedEmployeeIds, string? Message = null);

public sealed record InboxPage(IReadOnlyList<InboxItem> Items, int Total, int Page, int PageSize);

public sealed record InboxCounts(int PendingReceived, int UnreadMessages, int OpenAlerts);

/// <summary>One per module. The inbox router fans out to these; a new module (Leave, HR, …) plugs in by
/// registering a provider, with no change to Inbox or Conversations code.</summary>
public interface IInboxSourceProvider
{
    string Source { get; }

    /// <summary>Human name of the source module for the Source filter ("Work", "Leave", …).</summary>
    string Label { get; }

    /// <summary>Every item that involves the caller. The router applies the common filters, sorting and paging.</summary>
    Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct);

    /// <summary>Null when the item does not exist or the caller may not see it.</summary>
    Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct);

    Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);

    /// <summary>Runs the action through the module's own commands and rules.</summary>
    Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct);
}
