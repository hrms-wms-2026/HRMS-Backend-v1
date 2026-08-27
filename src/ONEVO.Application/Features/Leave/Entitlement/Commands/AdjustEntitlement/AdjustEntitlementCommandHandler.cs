using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.AdjustEntitlement;

public class AdjustEntitlementCommandHandler
    : IRequestHandler<AdjustEntitlementCommand, Result<LeaveEntitlementResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;

    public AdjustEntitlementCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
    }

    public async Task<Result<LeaveEntitlementResponse>> Handle(
        AdjustEntitlementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementResponse>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var entitlement = await _entitlements.GetTrackedByIdAsync(tenantId, request.EntitlementId, ct);
        if (entitlement is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.EntitlementNotFound);

        var oldBalance = LeaveEntitlementMapper.Remaining(entitlement);
        var newBalance = LeaveEntitlementMapper.Remaining(
            request.TotalDays, request.CarriedForwardDays, entitlement.UsedDays, entitlement.PendingDays);

        if (newBalance < 0m && !request.ConfirmNegativeRemaining)
        {
            return Result<LeaveEntitlementResponse>.Conflict(
                LeaveEntitlementMessages.NegativeRemaining(
                    request.TotalDays + request.CarriedForwardDays, entitlement.UsedDays));
        }

        var now = _dateTimeProvider.UtcNow;
        entitlement.TotalDays = request.TotalDays;
        entitlement.CarriedForwardDays = request.CarriedForwardDays;
        entitlement.ManualReason = request.Reason.Trim();
        entitlement.UpdatedAt = now;

        var audit = new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = entitlement.EmployeeId,
            LeaveTypeId = entitlement.LeaveTypeId,
            ChangeType = LeaveBalanceChangeTypes.Adjustment,
            DaysChanged = newBalance - oldBalance,
            BalanceAfter = newBalance,
            Reason = request.Reason.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        };

        await _entitlements.SaveWithAuditAsync(entitlement, audit, ct);

        var row = await _entitlements.GetRowByIdAsync(tenantId, entitlement.Id, ct);
        return Result<LeaveEntitlementResponse>.Success(
            LeaveEntitlementMapper.ToResponse(row!, warning: null, DateOnly.FromDateTime(now.UtcDateTime), null));
    }
}
