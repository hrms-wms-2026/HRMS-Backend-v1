using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

/// <summary>One onboarding approval as the Inbox shows it - a position access request or a seat increase request,
/// with the display names already joined. <see cref="RequesterEmployeeId"/> is the requester's employee record (null
/// for a user with none, e.g. a tenant admin).</summary>
public sealed record OnboardingInboxRow(
    string Kind, Guid Id, string Status, Guid RequestedByUserId, string RequesterName, Guid? RequesterEmployeeId,
    Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecisionNote,
    string? SubjectName, Guid? LegalEntityId, string? PositionName, string? DepartmentName, DateOnly? StartDate,
    int? RequestedSeats, int? SeatsAtRequest, string? Reason);

public static class OnboardingInboxRowKinds
{
    public const string PositionApproval = "position_approval";
    public const string SeatIncrease = "seat_increase";
}

public interface IOnboardingInboxRepository
{
    /// <summary>The onboarding approvals that involve the user: every pending one of a kind they approve
    /// (<paramref name="positionApprover"/>, <paramref name="seatApprover"/>), the ones they requested, and the
    /// ones they decided since <paramref name="decidedSince"/>.</summary>
    Task<IReadOnlyList<OnboardingInboxRow>> ListAsync(
        Guid tenantId, Guid userId, bool positionApprover, bool seatApprover, DateTimeOffset decidedSince, CancellationToken ct = default);

    Task<OnboardingInboxRow?> GetAsync(Guid tenantId, string kind, Guid id, CancellationToken ct = default);

    /// <summary>The names of everyone whose onboarding draft is waiting and will need a seat (waiting for a seat, or
    /// for position approval), oldest first.</summary>
    Task<IReadOnlyList<string>> ListWaitingForSeatNamesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface ISeatIncreaseRequestRepository
{
    Task AddAsync(SeatIncreaseRequest request, CancellationToken ct = default);

    Task<SeatIncreaseRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>One pending request covers every waiting person in the company.</summary>
    Task<bool> AnyPendingAsync(Guid tenantId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
