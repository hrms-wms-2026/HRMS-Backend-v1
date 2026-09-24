using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CreateLeavePolicyCommandHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public CreateLeavePolicyCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "LK Policy", null, It.IsAny<CancellationToken>()))
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
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid tenantId, Guid policyId, CancellationToken _) =>
            {
                var policy = new LeavePolicy
                {
                    Id = policyId,
                    TenantId = tenantId,
                    Name = "LK Policy",
                    Country = "LK",
                    AccrualMethod = LeaveAccrualMethods.Annual,
                    AccrualStart = LeaveAccrualStarts.Immediately,
                    ProrationMethod = LeaveProrationMethods.CalendarDays,
                    ApprovalMode = LeaveApprovalModes.AnyOne,
                    EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                return Task.FromResult<LeavePolicyAggregate?>(new LeavePolicyAggregate(policy, [], [], []));
            });
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesPolicyAggregate()
    {
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("LK Policy", result.Value!.Name);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.Is<LeavePolicy>(p => p.TenantId == _tenantId && p.Name == "LK Policy"),
            It.Is<IReadOnlyCollection<LeavePolicyLeaveType>>(rules => rules.Single().AnnualEntitlementDays == 20m),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.Is<IReadOnlyCollection<LeavePolicyLegalEntity>>(assignments => assignments.Single().LegalEntityId == _legalEntityId),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MonthlyAccrualAboveLeaveTypeLimit_Returns400()
    {
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand() with
        {
            AccrualMethod = LeaveAccrualMethods.Monthly,
            LeaveTypes = [new LeavePolicyTypeRuleInput(_leaveTypeId, 0m, 2m, null, null)]
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Monthly accrual", result.Error);
    }

    [Fact]
    public async Task Handle_MissingLeaveType_Returns404()
    {
        _repoMock.Setup(r => r.ListActiveLeaveTypesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("The selected leave type no longer exists.", result.Error);
    }

    [Fact]
    public async Task Handle_ExistingActiveLegalEntityAssignment_NotConfirmed_Returns409()
    {
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyLegalEntityConflict(_legalEntityId, "Acme Lanka", Guid.NewGuid(), "Old Policy")]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("Acme Lanka", result.Error);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.IsAny<LeavePolicy>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLeaveType>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLegalEntity>>(),
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingActiveLegalEntityAssignment_Confirmed_Replaces()
    {
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyLegalEntityConflict(_legalEntityId, "Acme Lanka", Guid.NewGuid(), "Old Policy")]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand() with { ConfirmReplaceExistingLegalEntityAssignments = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.IsAny<LeavePolicy>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLeaveType>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLegalEntity>>(),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == _legalEntityId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private CreateLeavePolicyCommand ValidCommand() => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new LeavePolicyTypeRuleInput(_leaveTypeId, 20m, null, 5m, 3)],
        [],
        [_legalEntityId],
        false);
}
