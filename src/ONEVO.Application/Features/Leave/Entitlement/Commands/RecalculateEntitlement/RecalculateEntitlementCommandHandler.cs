using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;

public class RecalculateEntitlementCommandHandler
    : IRequestHandler<RecalculateEntitlementCommand, Result<LeaveEntitlementResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly IEmployeeRepository _employees;
    private readonly ILeavePolicyRepository _policies;
    private readonly LeaveEntitlementCalculator _calculator;

    public RecalculateEntitlementCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements,
        IEmployeeRepository employees,
        ILeavePolicyRepository policies,
        LeaveEntitlementCalculator calculator)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
        _employees = employees;
        _policies = policies;
        _calculator = calculator;
    }

    public async Task<Result<LeaveEntitlementResponse>> Handle(
        RecalculateEntitlementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementResponse>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var entitlement = await _entitlements.GetTrackedByIdAsync(tenantId, request.EntitlementId, ct);
        if (entitlement is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.EntitlementNotFound);

        var employee = await _employees.GetByIdAsync(tenantId, entitlement.EmployeeId, ct);
        if (employee is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.EmployeeNotFound);

        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<LeaveEntitlementResponse>.Failure(LeaveEntitlementMessages.NoPolicyAssigned);

        var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, [legalEntityId], entitlement.Year, ct);
        if (!policies.TryGetValue(legalEntityId, out var policy))
            return Result<LeaveEntitlementResponse>.Failure(LeaveEntitlementMessages.NoPolicyAssigned);

        var typeRule = policy.LeaveTypes.FirstOrDefault(t => t.Rule.LeaveTypeId == entitlement.LeaveTypeId);
        if (typeRule is null)
            return Result<LeaveEntitlementResponse>.NotFound(LeaveEntitlementMessages.LeaveTypeNotFound);

        var assignment = policy.LegalEntities.First(x => x.Assignment.LegalEntityId == legalEntityId);
        var workingDays = LegalEntityMapper.ParseStandardWorkingDays(assignment.StandardWorkingDaysJson);
        var previous = await _entitlements.ListPreviousYearAsync(
            tenantId, entitlement.Year - 1, [entitlement.EmployeeId], ct);
        var priorRemaining = previous.TryGetValue((entitlement.EmployeeId, entitlement.LeaveTypeId), out var prior)
            ? LeaveEntitlementMapper.Remaining(prior)
            : 0m;

        var now = _dateTimeProvider.UtcNow;
        var calculation = _calculator.Calculate(new LeaveEntitlementCalculationInput(
            entitlement.Year,
            employee.HireDate,
            employee.ProbationEndDate,
            typeRule.Rule.AnnualEntitlementDays,
            priorRemaining,
            typeRule.Rule.CarryForwardMaxDays,
            typeRule.Rule.CarryForwardExpiryMonths,
            policy.Policy.AccrualMethod,
            policy.Policy.AccrualStart,
            policy.Policy.AccrualAfterNMonths,
            policy.Policy.ProrationMethod,
            policy.Policy.ProbationRestriction,
            policy.Policy.FirstYearReducedPercent,
            policy.Policy.MinimumTenureMonths,
            workingDays,
            DateOnly.FromDateTime(now.UtcDateTime)));

        if (calculation.SkipReason is not null)
            return Result<LeaveEntitlementResponse>.Failure(calculation.SkipReason);

        var oldBalance = LeaveEntitlementMapper.Remaining(entitlement);
        var newBalance = LeaveEntitlementMapper.Remaining(
            calculation.TotalDays, calculation.CarriedForwardDays, entitlement.UsedDays, entitlement.PendingDays);

        if (newBalance < 0m && !request.ConfirmNegativeRemaining)
        {
            return Result<LeaveEntitlementResponse>.Conflict(
                LeaveEntitlementMessages.NegativeRemaining(
                    calculation.TotalDays + calculation.CarriedForwardDays, entitlement.UsedDays));
        }

        entitlement.TotalDays = calculation.TotalDays;
        entitlement.CarriedForwardDays = calculation.CarriedForwardDays;
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
            Reason = "Recalculated from current leave policy",
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        };

        await _entitlements.SaveWithAuditAsync(entitlement, audit, ct);

        var row = await _entitlements.GetRowByIdAsync(tenantId, entitlement.Id, ct);
        var warnings = await _employees.ListLegalEntityChangeWarningsAsync(
            tenantId, [entitlement.EmployeeId], entitlement.Year, ct);
        return Result<LeaveEntitlementResponse>.Success(LeaveEntitlementMapper.ToResponse(
            row!,
            warnings.GetValueOrDefault(entitlement.EmployeeId),
            DateOnly.FromDateTime(now.UtcDateTime),
            calculation.CarryForwardExpiresOn));
    }
}
