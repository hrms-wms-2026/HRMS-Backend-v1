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

    public Task<LeaveEntitlementPlan> PlanAsync(
        Guid tenantId,
        int year,
        Guid? legalEntityId,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        throw new NotImplementedException("Leave cleanup Part 2 Task 3");
    }
}

public record LeaveEntitlementPlan(
    int Year,
    int EmployeeCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Lines,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped);
