using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingApprovalPreview;

/// <summary>Who will have to approve adding this person, for the Add People wizard: the position access approval
/// (when the position's access template requires it) and a seat increase (when every included seat is in use).</summary>
public sealed record GetOnboardingApprovalPreviewQuery(Guid? PositionId) : IRequest<Result<OnboardingApprovalPreviewResponse>>;

public sealed record OnboardingApproverResponse(Guid UserId, Guid? EmployeeId, string Name);

/// <summary><see cref="PositionApprovalBypassed"/>: the position needs approval but the caller can approve it
/// themselves (roles:manage), so finalizing skips it. <see cref="SeatsAvailable"/> is null when the seat policy is
/// not configured.</summary>
public sealed record OnboardingApprovalPreviewResponse(
    bool PositionApprovalRequired, bool PositionApprovalBypassed, IReadOnlyList<OnboardingApproverResponse> PositionApprovers,
    bool? SeatsAvailable, int? AvailableSeats, IReadOnlyList<OnboardingApproverResponse> SeatApprovers);

public sealed class GetOnboardingApprovalPreviewQueryHandler
    : IRequestHandler<GetOnboardingApprovalPreviewQuery, Result<OnboardingApprovalPreviewResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IPositionRepository _positions;
    private readonly ISeatEntitlementService _seats;
    private readonly OnboardingApprovers _approvers;

    public GetOnboardingApprovalPreviewQueryHandler(
        ICurrentUser currentUser, IPositionRepository positions, ISeatEntitlementService seats, OnboardingApprovers approvers)
    {
        _currentUser = currentUser;
        _positions = positions;
        _seats = seats;
        _approvers = approvers;
    }

    public async Task<Result<OnboardingApprovalPreviewResponse>> Handle(GetOnboardingApprovalPreviewQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var requiresApproval = request.PositionId is { } positionId
            && await _positions.GetAccessTemplateByPositionAsync(tenantId, positionId, ct) is { IsActive: true, RequiresApproval: true };
        var bypassed = requiresApproval && _currentUser.HasPermission(OnboardingApprovers.PositionApprovalPermission);
        var positionApprovers = requiresApproval && !bypassed
            ? await _approvers.PositionApproversAsync(tenantId, _currentUser.UserId, ct)
            : [];

        var seat = await _seats.EvaluateAsync(tenantId, ct);
        bool? seatsAvailable = seat.Status switch
        {
            SeatDecisionStatus.Approved => true,
            SeatDecisionStatus.Blocked => false,
            _ => null,
        };
        var seatApprovers = seatsAvailable == false
            ? await _approvers.SeatApproversAsync(tenantId, null, ct)
            : [];

        return Result<OnboardingApprovalPreviewResponse>.Success(new OnboardingApprovalPreviewResponse(
            requiresApproval, bypassed, positionApprovers.Select(Map).ToList(),
            seatsAvailable, seat.AvailableSeats, seatApprovers.Select(Map).ToList()));
    }

    private static OnboardingApproverResponse Map(OnboardingApprover a) => new(a.UserId, a.EmployeeId, a.Name);
}
