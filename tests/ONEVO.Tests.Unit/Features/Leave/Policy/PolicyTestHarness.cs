using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

internal sealed class PolicyTestHarness
{
    public static readonly DateOnly Today = new(2026, 10, 20);

    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid AnnualTypeId { get; } = Guid.NewGuid();
    public Guid SickTypeId { get; } = Guid.NewGuid();
    public Guid InactiveTypeId { get; } = Guid.NewGuid();
    public Guid CompanyId { get; } = Guid.NewGuid();
    public Guid SalesId { get; } = Guid.NewGuid();
    public Guid EngineeringId { get; } = Guid.NewGuid();
    public Guid DeveloperPositionId { get; } = Guid.NewGuid();
    public Guid EmployeeId { get; } = Guid.NewGuid();

    public FakeLeavePolicyRepository Repository { get; } = new();
    public InlineUnitOfWork UnitOfWork { get; } = new();
    public Mock<ICurrentUser> CurrentUser { get; } = new();
    public Mock<IDateTimeProvider> Clock { get; } = new();

    public PolicyTestHarness()
    {
        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        CurrentUser.SetupGet(x => x.UserId).Returns(UserId);
        Clock.SetupGet(x => x.Today).Returns(Today);
        Clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

        Repository.LeaveTypes.Add(Type(AnnualTypeId, "Annual Leave", true));
        Repository.LeaveTypes.Add(Type(SickTypeId, "Sick Leave", true));
        Repository.LeaveTypes.Add(Type(InactiveTypeId, "Old Leave", false));
        Repository.Targets[(LeavePolicyTargetKinds.LegalEntity, CompanyId)] = "Dapi Technologies";
        Repository.Targets[(LeavePolicyTargetKinds.Department, SalesId)] = "Sales";
        Repository.Targets[(LeavePolicyTargetKinds.Department, EngineeringId)] = "Engineering";
        Repository.Targets[(LeavePolicyTargetKinds.Position, DeveloperPositionId)] = "Developer";
        Repository.Targets[(LeavePolicyTargetKinds.Employee, EmployeeId)] = "Priya Nair";
    }

    public static LeavePolicyRulesInput Rules(
        decimal amountHours = 8m,
        string period = LeaveAmountPeriods.Month,
        IReadOnlyList<LeavePolicyBlackoutInput>? blackouts = null) =>
        new(amountHours, period, null, null, LeaveEligibility.Immediately, null, 0, 1m, null, null,
            LeaveApprovalModes.AnyOne, blackouts ?? []);

    public LeavePolicyAssignmentInput Company => new(LeavePolicyTargetKinds.LegalEntity, CompanyId);
    public LeavePolicyAssignmentInput Sales => new(LeavePolicyTargetKinds.Department, SalesId);
    public LeavePolicyAssignmentInput Engineering => new(LeavePolicyTargetKinds.Department, EngineeringId);
    public LeavePolicyAssignmentInput Developer => new(LeavePolicyTargetKinds.Position, DeveloperPositionId);

    /// <summary>Seeds a saved policy with version 1 (effective 2026-01-01) and open assignments from 2026-01-01.</summary>
    public LeavePolicy SeedPolicy(string name, Guid leaveTypeId, params LeavePolicyAssignmentInput[] targets)
    {
        var policy = new LeavePolicy { Id = Guid.NewGuid(), TenantId = TenantId, LeaveTypeId = leaveTypeId, Name = name };
        Repository.Policies.Add(policy);
        Repository.Versions.Add(new LeavePolicyVersion
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyId = policy.Id, VersionNumber = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1), AmountHours = 8m, AmountPeriod = LeaveAmountPeriods.Month,
            MinRequestHours = 1m, ApprovalMode = LeaveApprovalModes.AnyOne,
        });
        foreach (var target in targets)
        {
            Repository.Assignments.Add(new LeavePolicyAssignment
            {
                Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyId = policy.Id, LeaveTypeId = leaveTypeId,
                TargetKind = target.TargetKind, TargetId = target.TargetId, EffectiveFrom = new DateOnly(2026, 1, 1),
            });
        }

        return policy;
    }

    private LeaveType Type(Guid id, string name, bool active) => new()
    {
        Id = id, TenantId = TenantId, Name = name, Code = name[..3].ToUpperInvariant(), IsActive = active,
    };
}
