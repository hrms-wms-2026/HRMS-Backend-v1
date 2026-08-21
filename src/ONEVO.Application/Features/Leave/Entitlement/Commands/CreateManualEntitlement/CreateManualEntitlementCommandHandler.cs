using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.CreateManualEntitlement;

public class CreateManualEntitlementCommandHandler
    : IRequestHandler<CreateManualEntitlementCommand, Result<LeaveEntitlementResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ILeaveEntitlementRepository _entitlements;

    public CreateManualEntitlementCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IEmployeeRepository employees,
        ILeaveTypeRepository leaveTypes,
        ILeaveEntitlementRepository entitlements)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _employees = employees;
        _leaveTypes = leaveTypes;
        _entitlements = entitlements;
    }

    public async Task<Result<LeaveEntitlementResponse>> Handle(
        CreateManualEntitlementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementResponse>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var now = _dateTimeProvider.UtcNow;

        if (await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct) is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.EmployeeNotFound);

        if (await _leaveTypes.GetByIdAsync(tenantId, request.LeaveTypeId, ct) is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.LeaveTypeNotFound);

        if (await _entitlements.GetTrackedByEmployeeTypeYearAsync(
                tenantId, request.EmployeeId, request.LeaveTypeId, request.Year, ct) is not null)
        {
            return Result<LeaveEntitlementResponse>.Conflict(LeaveEntitlementMessages.DuplicateEmployeeTypeYear);
        }

        var entitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            Year = request.Year,
            TotalDays = request.TotalDays,
            UsedDays = 0m,
            PendingDays = 0m,
            CarriedForwardDays = request.CarriedForwardDays,
            Source = LeaveEntitlementSources.Manual,
            ManualReason = request.Reason.Trim(),
            CreatedAt = now
        };

        var remaining = LeaveEntitlementMapper.Remaining(entitlement);
        var audit = new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            ChangeType = LeaveBalanceChangeTypes.Accrual,
            DaysChanged = remaining,
            BalanceAfter = remaining,
            Reason = request.Reason.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        };

        try
        {
            await _entitlements.AddManualAsync(entitlement, audit, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<LeaveEntitlementResponse>.Conflict(LeaveEntitlementMessages.DuplicateEmployeeTypeYear);
        }

        var row = await _entitlements.GetRowByIdAsync(tenantId, entitlement.Id, ct);
        return Result<LeaveEntitlementResponse>.Success(
            LeaveEntitlementMapper.ToResponse(row!, warning: null, DateOnly.FromDateTime(now.UtcDateTime), null));
    }
}
