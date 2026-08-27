using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class PreviewGenerateEntitlementsQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILeavePolicyRepository> _policies = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlements = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();

    [Fact]
    public async Task Handle_UsesConfiguredPolicyValuesForPreview()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = CreateEmployee(tenantId, legalEntityId);
        var policy = CreatePolicyAggregate(tenantId, legalEntityId, 17.5m, 4m);

        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
        _employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([employee]);
        _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });
        _entitlements.Setup(x => x.ListExistingAsync(tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());
        _employees.Setup(x => x.ListLegalEntityChangeWarningsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var result = await BuildHandler().Handle(new PreviewGenerateEntitlementsQuery(2026, legalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Lines.Should().ContainSingle();
        result.Value.Lines[0].TotalDays.Should().Be(17.5m);
    }

    [Fact]
    public async Task Handle_SkipsEmployeeWithoutActivePolicy()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = CreateEmployee(tenantId, legalEntityId);

        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
        _employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([employee]);
        _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());
        _entitlements.Setup(x => x.ListExistingAsync(tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());
        _employees.Setup(x => x.ListLegalEntityChangeWarningsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var result = await BuildHandler().Handle(new PreviewGenerateEntitlementsQuery(2026, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Skipped.Should().Contain(x => x.Reason == LeaveEntitlementMessages.NoPolicyAssigned);
    }

    private PreviewGenerateEntitlementsQueryHandler BuildHandler() => new(
        _currentUser.Object,
        _dateTime.Object,
        new LeaveEntitlementPlanner(
            _employees.Object,
            _policies.Object,
            _entitlements.Object,
            new LeaveEntitlementCalculator(new LeaveWorkingDayCounter())));

    internal static Employee CreateEmployee(Guid tenantId, Guid legalEntityId, DateOnly? hireDate = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Priya",
        LastName = "Nair",
        Email = "priya@test.dev",
        LegalEntityId = legalEntityId,
        HireDate = hireDate ?? new DateOnly(2024, 1, 1),
        EmploymentStatusId = EmploymentStatusIds.Active
    };

    internal static LeavePolicyAggregate CreatePolicyAggregate(
        Guid tenantId, Guid legalEntityId, decimal annualEntitlementDays, decimal? carryForwardMaxDays)
    {
        var policy = new LeavePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "UK Policy",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        var leaveTypeId = Guid.NewGuid();
        var assignment = new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policy.Id,
            LegalEntityId = legalEntityId,
            EffectiveDate = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        var rule = new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policy.Id,
            LeaveTypeId = leaveTypeId,
            AnnualEntitlementDays = annualEntitlementDays,
            CarryForwardMaxDays = carryForwardMaxDays,
            CarryForwardExpiryMonths = 3
        };
        return new LeavePolicyAggregate(
            policy,
            [new LeavePolicyLeaveTypeWithType(rule, "Annual Leave", "AL")],
            [],
            [new LeavePolicyLegalEntityWithName(assignment, "Acme UK", "[1,2,3,4,5]")]);
    }
}
