using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Inbox.Entities;

/// <summary>Values of <see cref="InboxDecisionJournalEntry.Action"/>. Resolutions are "resolve:{outcome}".</summary>
public static class InboxJournalActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Acknowledge = "acknowledge";
    public const string Escalate = "escalate";
    public const string Cancel = "cancel";
    public const string Revert = "revert";
    public const string ResolvePrefix = "resolve:";

    public static string Resolve(string outcome) => ResolvePrefix + outcome;
}

/// <summary>
/// One decision taken on an inbox item (any module). Written by the item's provider in the same
/// transaction as the module change; feeds the item's Steps, the History tab and the 30-minute revert.
/// BeforeJson/AfterJson hold the minimal snapshot the item's reverter needs.
/// </summary>
public class InboxDecisionJournalEntry : BaseEntity
{
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorEmployeeId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    /// <summary>Set on the original row once it has been reverted: the id of the "revert" row.</summary>
    public Guid? RevertedByJournalId { get; set; }
}
