using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Services;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;

public class RecalculateEntitlementCommandHandler
    : IRequestHandler<RecalculateEntitlementCommand, Result<LeaveEntitlementResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeaveCreditService _credits;

    public RecalculateEntitlementCommandHandler(
        ICurrentUser currentUser, IDateTimeProvider clock,
        ILeaveEntitlementRepository entitlements, ILeaveCreditService credits)
    {
        _currentUser = currentUser;
        _clock = clock;
        _entitlements = entitlements;
        _credits = credits;
    }

    public async Task<Result<LeaveEntitlementResponse>> Handle(
        RecalculateEntitlementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementResponse>.Forbidden("Tenant context missing.");

        var before = await _entitlements.GetRowByIdAsync(_currentUser.TenantId, request.EntitlementId, ct);
        if (before is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.EntitlementNotFound);
        var asOf = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        await _credits.CreditAsync(_currentUser.TenantId, [before.Entitlement.EmployeeId],
            before.Entitlement.Year, asOf, false, _currentUser.UserId, ct);
        var after = await _entitlements.GetRowByIdAsync(_currentUser.TenantId, request.EntitlementId, ct);
        return Result<LeaveEntitlementResponse>.Success(
            LeaveEntitlementMapper.ToResponse(after!, null, asOf, null));
    }
}
