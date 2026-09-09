using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public class LeaveEntitlementPlanner
{
    private static readonly int[] EntitledEmploymentStatuses =
        [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly IEmployeeRepository _employees;
    private readonly ILeavePolicyRepository _policies;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly LeaveEntitlementCalculator _calculator;

    public LeaveEntitlementPlanner(
        IEmployeeRepository employees,
        ILeavePolicyRepository policies,
        ILeaveEntitlementRepository entitlements,
        LeaveEntitlementCalculator calculator)
    {
        _employees = employees;
        _policies = policies;
        _entitlements = entitlements;
        _calculator = calculator;
    }

    public async Task<LeaveEntitlementPlan> PlanAsync(
        Guid tenantId,
        int year,
        Guid? legalEntityId,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        var employees = await _employees.ListActiveByLegalEntityAsync(tenantId, legalEntityId, ct);
        var employeeIds = employees.Select(e => e.Id).ToArray();
        var legalEntityIds = employees.Select(e => e.LegalEntityId).OfType<Guid>().Distinct().ToArray();
        var policiesByLegalEntity = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, legalEntityIds, year, ct);
        var existing = (await _entitlements.ListExistingAsync(tenantId, year, employeeIds, ct))
            .Select(e => (e.EmployeeId, e.LeaveTypeId))
            .ToHashSet();
        var previous = await _entitlements.ListPreviousYearAsync(tenantId, year - 1, employeeIds, ct);
        var warnings = await _employees.ListLegalEntityChangeWarningsAsync(tenantId, employeeIds, year, ct);
        var legalEntitiesById = (await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, legalEntityIds, ct))
            .ToDictionary(e => e.Id);

        var lines = new List<LeaveEntitlementGenerationLineResponse>();
        var skipped = new List<LeaveEntitlementGenerationSkipResponse>();

        foreach (var employee in employees)
        {
            var name = LeaveEntitlementMapper.EmployeeName(employee.FirstName, employee.LastName);
            if (!EntitledEmploymentStatuses.Contains(employee.EmploymentStatusId))
            {
                skipped.Add(new(employee.Id, name, LeaveEntitlementMessages.InactiveEmployee));
                continue;
            }

            if (employee.LegalEntityId is not Guid employeeLegalEntityId
                || !policiesByLegalEntity.TryGetValue(employeeLegalEntityId, out var policy))
            {
                skipped.Add(new(employee.Id, name, LeaveEntitlementMessages.NoPolicyAssigned));
                continue;
            }

            if (!legalEntitiesById.TryGetValue(employeeLegalEntityId, out var legalEntity))
            {
                skipped.Add(new(employee.Id, name, LeaveRequestMessages.WorkWindowRequired));
                continue;
            }

            var workHours = WorkDayHoursCalculator.TryCompute(
                legalEntity.WorkStartTime, legalEntity.WorkEndTime, legalEntity.BreakDurationMinutes);
            if (workHours is null or <= 0m)
            {
                skipped.Add(new(employee.Id, name, LeaveRequestMessages.WorkWindowRequired));
                continue;
            }

            IReadOnlyCollection<int> workingDays;
            try
            {
                workingDays = LegalEntityMapper.ParseStandardWorkingDays(legalEntity.StandardWorkingDays);
            }
            catch (Exception)
            {
                skipped.Add(new(employee.Id, name, "Legal entity working days are not configured"));
                continue;
            }

            foreach (var typeRule in policy.LeaveTypes)
            {
                if (existing.Contains((employee.Id, typeRule.Rule.LeaveTypeId)))
                {
                    skipped.Add(new(employee.Id, name, LeaveEntitlementMessages.AlreadyExists(year)));
                    continue;
                }

                var priorRemainingHours = previous.TryGetValue((employee.Id, typeRule.Rule.LeaveTypeId), out var prior)
                    ? LeaveEntitlementMapper.Remaining(prior)
                    : 0m;
                var priorRemainingDays = decimal.Round(
                    priorRemainingHours / workHours.Value, 1, MidpointRounding.AwayFromZero);

                var calculation = _calculator.Calculate(new LeaveEntitlementCalculationInput(
                    year,
                    employee.HireDate,
                    employee.ProbationEndDate,
                    typeRule.Rule.AnnualEntitlementDays,
                    priorRemainingDays,
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
                    asOfDate));

                if (calculation.SkipReason is not null)
                {
                    skipped.Add(new(employee.Id, name, calculation.SkipReason));
                    continue;
                }

                var totalHours = decimal.Round(
                    calculation.TotalDays * workHours.Value, 2, MidpointRounding.AwayFromZero);
                var carryHours = decimal.Round(
                    calculation.CarriedForwardDays * workHours.Value, 2, MidpointRounding.AwayFromZero);
                var forfeitedHours = decimal.Round(
                    calculation.ForfeitedDays * workHours.Value, 2, MidpointRounding.AwayFromZero);

                lines.Add(new LeaveEntitlementGenerationLineResponse(
                    employee.Id,
                    employee.EmployeeNumber,
                    name,
                    typeRule.Rule.LeaveTypeId,
                    typeRule.LeaveTypeName,
                    totalHours,
                    carryHours,
                    totalHours + carryHours,
                    calculation.ProbationRestrictionApplied,
                    forfeitedHours,
                    calculation.CarryForwardExpiresOn,
                    warnings.GetValueOrDefault(employee.Id)));
            }
        }

        return new LeaveEntitlementPlan(year, employees.Count, lines, skipped);
    }

    public static DateOnly? CarryExpiryFromPolicy(LeavePolicyAggregate? policy, Guid leaveTypeId, int year)
    {
        var months = policy?.LeaveTypes
            .FirstOrDefault(t => t.Rule.LeaveTypeId == leaveTypeId)
            ?.Rule.CarryForwardExpiryMonths;
        return LeaveEntitlementCalculator.CarryForwardExpiryDate(year, months);
    }
}

public record LeaveEntitlementPlan(
    int Year,
    int EmployeeCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Lines,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped);
