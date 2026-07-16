using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public class SubscriptionTrialAndGracePeriodTests
{
    private static readonly Guid PlanId = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001");
    private static readonly Guid OwnerRoleId = Guid.Parse("b1b2c3d4-0001-0001-0001-000000000099");

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ITenantAuthPolicyRepository> _authPolicies = new();
    private readonly Mock<ISubscriptionPlanRepository> _plans = new();
    private readonly Mock<ITenantSubscriptionRepository> _subscriptions = new();
    private readonly Mock<IDefaultRoleSeeder> _roleSeeder = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ITenantOwnerInvitationService> _invitationService = new();
    private readonly Mock<ITenantSetupSelectionRepository> _setupSelections = new();

    private readonly DateTimeOffset _now = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private CreateTenantCommandHandler BuildHandler() => new(
        _tenants.Object,
        _legalEntities.Object,
        _authPolicies.Object,
        _plans.Object,
        _subscriptions.Object,
        _roleSeeder.Object,
        _currentUser.Object,
        _unitOfWork.Object,
        _clock.Object,
        _invitationService.Object,
        _setupSelections.Object);

    private static CreateTenantCommand BuildCommand(int? trialPeriodDays = null, int? unpaidGracePeriodDays = null) =>
        new("Acme Corp", "acme-corp", "office_it", "51-200",
            "Acme Legal Ltd", "REG123", "LK", "Asia/Colombo", "LKR",
            new SubscriptionInfo(PlanId, "monthly", "subscription", trialPeriodDays, unpaidGracePeriodDays),
            null,
            null);

    private TenantSubscription? _capturedSubscription;

    public SubscriptionTrialAndGracePeriodTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _tenants.Setup(t => t.SlugExistsAsync("acme-corp", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _plans.Setup(p => p.GetByIdAsync(PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionPlan
            {
                Id = PlanId,
                Name = "Starter",
                Code = "starter",
                Tier = "standard",
                Currency = "USD",
                CompanySizeRange = "51-200",
                CalculatedMonthlyPrice = 100m,
                CalculatedAnnualPrice = 1000m,
                IncludedModulesJson = "[\"core\"]",
                TrialPeriodDays = 30,
                UnpaidGracePeriodDays = 7
            });

        _roleSeeder.Setup(r => r.SeedOwnerRoleAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, IReadOnlyList<string> _, CancellationToken _) =>
                new Role
                {
                    Id = OwnerRoleId,
                    TenantId = tenantId,
                    Name = "Owner",
                    IsSystem = true
                });

        _setupSelections
            .Setup(r => r.AddRangeAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Capture the subscription entity passed to AddAsync
        _subscriptions
            .Setup(s => s.AddAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<TenantSubscription, CancellationToken>((sub, _) => _capturedSubscription = sub)
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_NoPlanOverride_UsesPlansDefaultTrialDays()
    {
        // No overrides → plan defaults (30 trial days, 7 grace days)
        var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _capturedSubscription.Should().NotBeNull();
        _capturedSubscription!.Status.Should().Be("trialing");
        _capturedSubscription.TrialStartDate.Should().Be(_now);
        _capturedSubscription.TrialEndDate.Should().Be(_now.AddDays(30));
        _capturedSubscription.UnpaidGracePeriodDays.Should().Be(7);
    }

    [Fact]
    public async Task Handle_OperatorOverrideTrialDays_UsesOverride()
    {
        // Operator supplies 14 trial days instead of plan's 30
        var result = await BuildHandler().Handle(BuildCommand(trialPeriodDays: 14), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _capturedSubscription.Should().NotBeNull();
        _capturedSubscription!.Status.Should().Be("trialing");
        _capturedSubscription.TrialStartDate.Should().Be(_now);
        _capturedSubscription.TrialEndDate.Should().Be(_now.AddDays(14));
    }

    [Fact]
    public async Task Handle_NoPlanOverride_UsesPlansDefaultGracePeriod()
    {
        // No overrides → grace period copied from plan default (7)
        var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _capturedSubscription.Should().NotBeNull();
        _capturedSubscription!.UnpaidGracePeriodDays.Should().Be(7);
    }

    [Fact]
    public async Task Handle_NegativeTrialDays_ReturnsValidationFailure()
    {
        var result = await BuildHandler().Handle(BuildCommand(trialPeriodDays: -1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("non-negative");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NegativeGracePeriodDays_ReturnsValidationFailure()
    {
        var result = await BuildHandler().Handle(BuildCommand(unpaidGracePeriodDays: -1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("non-negative");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ZeroTrialDays_StatusIsActive_NoTrialDates()
    {
        // Zero trial days → status "active", no trial start/end dates
        var result = await BuildHandler().Handle(BuildCommand(trialPeriodDays: 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _capturedSubscription.Should().NotBeNull();
        _capturedSubscription!.Status.Should().Be("active");
        _capturedSubscription.TrialStartDate.Should().BeNull();
        _capturedSubscription.TrialEndDate.Should().BeNull();
    }
}
