using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;

public class GenerateEntitlementsCommandHandler
    : IRequestHandler<GenerateEntitlementsCommand, Result<LeaveEntitlementGenerationResultResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly LeaveEntitlementPlanner _planner;
    private readonly ILeaveEntitlementRepository _entitlements;

    public GenerateEntitlementsCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        LeaveEntitlementPlanner planner,
        ILeaveEntitlementRepository entitlements)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _planner = planner;
        _entitlements = entitlements;
    }

    public async Task<Result<LeaveEntitlementGenerationResultResponse>> Handle(
        GenerateEntitlementsCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveEntitlementGenerationResultResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveEntitlementGenerationResultResponse>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var now = _dateTimeProvider.UtcNow;
        var asOfDate = DateOnly.FromDateTime(now.UtcDateTime);
        var plan = await _planner.PlanAsync(tenantId, request.Year, request.LegalEntityId, asOfDate, ct);

        if (plan.Lines.Count > 0)
        {
            var writeSets = plan.Lines.Select(line =>
            {
                var entitlement = new LeaveEntitlement
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = line.EmployeeId,
                    LeaveTypeId = line.LeaveTypeId,
                    Year = request.Year,
                    TotalDays = line.TotalDays,
                    UsedDays = 0m,
                    PendingDays = 0m,
                    CarriedForwardDays = line.CarriedForwardDays,
                    Source = LeaveEntitlementSources.Auto,
                    CreatedAt = now
                };

                var audits = new List<LeaveBalanceAudit>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EmployeeId = line.EmployeeId,
                        LeaveTypeId = line.LeaveTypeId,
                        ChangeType = LeaveBalanceChangeTypes.Accrual,
                        DaysChanged = line.TotalDays + line.CarriedForwardDays,
                        BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                        Reason = "Generated from active leave policy",
                        CreatedAt = now,
                        CreatedBy = _currentUser.UserId
                    }
                };

                if (line.ForfeitedDays > 0m)
                {
                    audits.Add(new LeaveBalanceAudit
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EmployeeId = line.EmployeeId,
                        LeaveTypeId = line.LeaveTypeId,
                        ChangeType = LeaveBalanceChangeTypes.Forfeiture,
                        DaysChanged = -line.ForfeitedDays,
                        BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                        Reason = "Carry-forward cap applied during generation",
                        CreatedAt = now,
                        CreatedBy = _currentUser.UserId
                    });
                }

                return new LeaveEntitlementWriteSet(entitlement, audits);
            }).ToList();

            try
            {
                await _entitlements.AddGeneratedAsync(writeSets, ct);
            }
            catch (UniqueConstraintConflictException)
            {
                return Result<LeaveEntitlementGenerationResultResponse>.Conflict(
                    LeaveEntitlementMessages.DuplicateEmployeeTypeYear);
            }
        }

        return Result<LeaveEntitlementGenerationResultResponse>.Success(new LeaveEntitlementGenerationResultResponse(
            plan.Year,
            plan.Lines.Count,
            plan.Skipped.Count,
            0,
            plan.Lines,
            plan.Skipped,
            []));
    }
}
