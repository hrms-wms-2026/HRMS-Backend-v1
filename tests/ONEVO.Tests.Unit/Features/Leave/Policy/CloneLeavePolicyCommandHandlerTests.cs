using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CloneLeavePolicyCommandHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourcePolicyId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public CloneLeavePolicyCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));

        var source = new LeavePolicy
        {
            Id = _sourcePolicyId,
            TenantId = _tenantId,
            Name = "Source Policy",
            Country = "UK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var sourceTypeRule = new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LeavePolicyId = _sourcePolicyId,
            LeaveTypeId = _leaveTypeId,
            AnnualEntitlementDays = 20m,
            CarryForwardMaxDays = 5m,
            CarryForwardExpiryMonths = 3
        };

        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _sourcePolicyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicyAggregate(
                source,
                [new LeavePolicyLeaveTypeWithType(sourceTypeRule, "Annual Leave", "ANNUAL")],
                [new LeavePolicyBlackoutPeriod
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    LeavePolicyId = _sourcePolicyId,
                    StartDate = new DateOnly(2026, 12, 24),
                    EndDate = new DateOnly(2026, 12, 26),
                    Reason = "Peak closure"
                }],
                []));
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "LK Copy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.ListActiveLeaveTypesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType
            {
                Id = _leaveTypeId,
                TenantId = _tenantId,
                Name = "Annual Leave",
                Code = "ANNUAL",
                DefaultDaysPerYear = 20m,
                IsActive = true
            }]);
        _repoMock.Setup(r => r.ListActiveLegalEntitiesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                Name = "Acme Lanka",
                CountryCode = "LKA",
                CurrencyCode = "LKR",
                IsActive = true
            }]);
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, It.Is<Guid>(id => id != _sourcePolicyId), It.IsAny<CancellationToken>()))
            .Returns((Guid tenantId, Guid policyId, CancellationToken _) =>
            {
                var clone = new LeavePolicy
                {
                    Id = policyId,
                    TenantId = tenantId,
                    Name = "LK Copy",
                    Country = "LK",
                    AccrualMethod = LeaveAccrualMethods.Annual,
                    AccrualStart = LeaveAccrualStarts.Immediately,
                    ProrationMethod = LeaveProrationMethods.CalendarDays,
                    ApprovalMode = LeaveApprovalModes.AnyOne,
                    EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                return Task.FromResult<LeavePolicyAggregate?>(new LeavePolicyAggregate(clone, [], [], []));
            });
    }

    [Fact]
    public async Task Handle_ValidCommand_CopiesRulesAndBlackoutPeriods()
    {
        var handler = new CloneLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.Is<LeavePolicy>(p => p.Name == "LK Copy" && p.Country == "LK"),
            It.Is<IReadOnlyCollection<LeavePolicyLeaveType>>(rules => rules.Single().AnnualEntitlementDays == 20m),
            It.Is<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(periods => periods.Single().Reason == "Peak closure"),
            It.Is<IReadOnlyCollection<LeavePolicyLegalEntity>>(assignments => assignments.Single().LegalEntityId == _legalEntityId),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SourceMissing_Returns404()
    {
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _sourcePolicyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeavePolicyAggregate?)null);
        var handler = new CloneLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveSourceLeaveType_Returns404()
    {
        _repoMock.Setup(r => r.ListActiveLeaveTypesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new CloneLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("The selected leave type no longer exists.", result.Error);
    }

    private CloneLeavePolicyCommand ValidCommand() =>
        new(_sourcePolicyId, "LK Copy", "LK", [_legalEntityId], new DateOnly(2026, 1, 1), false);
}
