using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ClockInPolicyEntity = ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.ClockInPolicies;

public class ClockInPolicyCommandHandlerTests
{
    private readonly Mock<IClockInPolicyRepository> _policies = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IClockInPolicyScopeMembershipValidator> _scopeMembership = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public ClockInPolicyCommandHandlerTests()
    {
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _scopeMembership
            .Setup(s => s.ValidateAsync(_tenantId, _legalEntityId, It.IsAny<ClockInPolicyScopeInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Application.Common.Models.Result?)null);
        _policies
            .Setup(p => p.HasOverlappingActiveScopeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<Guid[]?>(), It.IsAny<Guid[]?>(), It.IsAny<Guid[]?>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task Create_Succeeds_And_Maps_Hybrid_To_Either_Fields()
    {
        ClockInPolicyEntity? saved = null;
        _policies
            .Setup(p => p.AddAsync(It.IsAny<ClockInPolicyEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ClockInPolicyEntity, CancellationToken>((p, _) => saved = p)
            .Returns(Task.CompletedTask);

        var handler = new CreateClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _scopeMembership.Object,
            _currentUser.Object, _clock.Object);

        var result = await handler.Handle(ValidCreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.True(saved!.EitherWebEnabled);
        Assert.Equal(ClockInPolicyEntity.HybridSourceEmployeeChoice, saved.EitherSourceRule);
        Assert.True(result.Value!.WorkAreaRules.Hybrid.WebEnabled);
        Assert.DoesNotContain(
            result.Value.WorkAreaRules.GetType().GetProperties().Select(p => p.Name),
            n => n.Contains("Either", StringComparison.Ordinal));
        Assert.Contains(
            result.Value.WorkAreaRules.GetType().GetProperties().Select(p => p.Name),
            n => n == "Hybrid");
        _policies.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Rejects_Wrong_Tenant_LegalEntity()
    {
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var handler = new CreateClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _scopeMembership.Object,
            _currentUser.Object, _clock.Object);

        var result = await handler.Handle(ValidCreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Create_Rejects_Department_Outside_LegalEntity()
    {
        _scopeMembership
            .Setup(s => s.ValidateAsync(_tenantId, _legalEntityId, It.IsAny<ClockInPolicyScopeInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result.NotFound("Department missing."));

        var handler = new CreateClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _scopeMembership.Object,
            _currentUser.Object, _clock.Object);

        var cmd = ValidCreateCommand() with
        {
            Scope = new ClockInPolicyScopeInput(
                ClockInPolicyEntity.ScopeDepartment, [Guid.NewGuid()], null, null)
        };
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds()
    {
        var policyId = Guid.NewGuid();
        var existing = new ClockInPolicyEntity
        {
            Id = policyId,
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            Name = "Old",
            ScopeType = ClockInPolicyEntity.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EitherSourceRule = ClockInPolicyEntity.HybridSourceOnsite,
            FieldPhotoRequirement = ClockInPolicyEntity.FieldPhotoOff,
            NotificationRecipientResolver = ClockInPolicyEntity.NotificationManagementCoverageOwner,
            IsActive = true,
            CreatedById = _userId,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _policies
            .Setup(p => p.GetTrackedByIdForLegalEntityAsync(
                _tenantId, _legalEntityId, policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new UpdateClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _scopeMembership.Object,
            _currentUser.Object, _clock.Object);

        var result = await handler.Handle(
            ValidUpdateCommand(policyId) with { Name = "Updated Policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Policy", result.Value!.Name);
        Assert.Equal("Updated Policy", existing.Name);
    }

    [Fact]
    public async Task Archive_Then_Restore_Behavior()
    {
        var policyId = Guid.NewGuid();
        var existing = new ClockInPolicyEntity
        {
            Id = policyId,
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            Name = "Policy",
            ScopeType = ClockInPolicyEntity.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EitherSourceRule = ClockInPolicyEntity.HybridSourceEmployeeChoice,
            FieldPhotoRequirement = ClockInPolicyEntity.FieldPhotoOff,
            NotificationRecipientResolver = ClockInPolicyEntity.NotificationManagementCoverageOwner,
            IsActive = true,
            CreatedById = _userId,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _policies
            .Setup(p => p.GetTrackedByIdForLegalEntityAsync(
                _tenantId, _legalEntityId, policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var archiveHandler = new ArchiveClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _currentUser.Object, _clock.Object);
        var archiveResult = await archiveHandler.Handle(
            new ArchiveClockInPolicyCommand(_legalEntityId, policyId), CancellationToken.None);

        Assert.True(archiveResult.IsSuccess);
        Assert.False(existing.IsActive);

        var restoreHandler = new RestoreClockInPolicyCommandHandler(
            _policies.Object, _legalEntities.Object, _currentUser.Object, _clock.Object);
        var restoreResult = await restoreHandler.Handle(
            new RestoreClockInPolicyCommand(_legalEntityId, policyId), CancellationToken.None);

        Assert.True(restoreResult.IsSuccess);
        Assert.True(existing.IsActive);
    }

    private CreateClockInPolicyCommand ValidCreateCommand()
        => new(
            _legalEntityId,
            "Default Clock-in Policy",
            new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeFullCompany, null, null, null),
            new DateOnly(2026, 8, 21),
            null,
            true,
            100,
            ValidWorkAreaRules(),
            true,
            ClockInPolicyEntity.NotificationManagementCoverageOwner,
            [new LateDeductionRuleInput(15, 0, Guid.NewGuid())],
            true);

    private UpdateClockInPolicyCommand ValidUpdateCommand(Guid policyId)
        => new(
            _legalEntityId,
            policyId,
            "Default Clock-in Policy",
            new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeFullCompany, null, null, null),
            new DateOnly(2026, 8, 21),
            null,
            true,
            100,
            ValidWorkAreaRules(),
            true,
            ClockInPolicyEntity.NotificationManagementCoverageOwner,
            [new LateDeductionRuleInput(15, 0, Guid.NewGuid())],
            true);

    private static WorkAreaRulesInput ValidWorkAreaRules()
        => new(
            new WorkAreaSourceRulesInput(true, false, false, false),
            new WorkAreaSourceRulesInput(false, true, true, true),
            new HybridWorkAreaRulesInput(false, true, true, true, true, ClockInPolicyEntity.HybridSourceEmployeeChoice),
            new FieldWorkAreaRulesInput(false, true, true, ClockInPolicyEntity.FieldPhotoRequired));
}
