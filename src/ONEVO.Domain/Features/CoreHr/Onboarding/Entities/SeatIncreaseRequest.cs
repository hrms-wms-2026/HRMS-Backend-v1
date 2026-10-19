using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>A request to raise the company's included seat count, raised automatically when an onboarding
/// draft (wizard or bulk) has to wait because every seat is in use. One pending request covers everyone waiting:
/// billing managers (billing:manage) approve it in the Inbox, which raises <c>TenantSubscription.IncludedSeats</c> by
/// the number of people waiting at that moment (stored in <see cref="RequestedSeats"/>).</summary>
public class SeatIncreaseRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>The draft that raised the request (the first to wait); null when raised by hand.</summary>
    public Guid? OnboardingDraftId { get; set; }
    public int RequestedSeats { get; set; } = 1;

    /// <summary>The included seat count when the request was raised (for the approver's context).</summary>
    public int? SeatsAtRequest { get; set; }
    public string Status { get; set; } = SeatIncreaseRequestStatus.Pending;
    public string? Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

public static class SeatIncreaseRequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
