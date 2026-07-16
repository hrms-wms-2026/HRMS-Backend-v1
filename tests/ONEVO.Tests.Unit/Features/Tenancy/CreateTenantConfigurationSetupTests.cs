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
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public class CreateTenantConfigurationSetupTests
{
    private static readonly Guid PlanId = Guid.Parse("a1b2c3d4-0002-0002-0002-000000000002");
    private static readonly Guid OwnerRoleId = Guid.Parse("b1b2c3d4-0002-0002-0002-000000000099");

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

    private static CreateTenantCommand CommandWith(TenantConfigurationSetupInfo? setup) =>
        new("Acme Corp", "acme-corp", "office_it", "51-200",
            "Acme Legal Ltd", "REG123", "LK", "Asia/Colombo", "LKR",
            new SubscriptionInfo(PlanId, "monthly", "subscription"),
            setup,
            null);

    public CreateTenantConfigurationSetupTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
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
                IncludedModulesJson = "[\"core\"]"
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
    }

    // 1. Valid setup options create one TenantSetupSelection per option
    [Fact]
    public async Task Handle_ValidSetupOptions_CallsAddRangeWithAllKeys()
    {
        var options = new List<string>
        {
            TenantSetupOptionKeys.JobFamilyCreation,
            TenantSetupOptionKeys.EmployeeInvite
        };
        var command = CommandWith(new TenantConfigurationSetupInfo(options));

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _setupSelections.Verify(r => r.AddRangeAsync(
            It.IsAny<Guid>(),
            It.Is<IReadOnlyList<string>>(keys =>
                keys.Count == 2 &&
                keys.Contains(TenantSetupOptionKeys.JobFamilyCreation) &&
                keys.Contains(TenantSetupOptionKeys.EmployeeInvite)),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 2. Duplicate setup options are de-duplicated in the repository (only one row per key)
    [Fact]
    public async Task Handle_DuplicateSetupOptionKeys_PassedToRepositoryForDeduplication()
    {
        // The handler passes the list as-is; deduplication (.Distinct()) is in EfTenantSetupSelectionRepository.
        // We verify the repository is called once (not once per duplicate), meaning the handler
        // delegates dedup to the repository correctly.
        var options = new List<string>
        {
            TenantSetupOptionKeys.EmployeeInvite,
            TenantSetupOptionKeys.EmployeeInvite
        };
        var command = CommandWith(new TenantConfigurationSetupInfo(options));

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Repository is called once (not twice); it internally deduplicates with .Distinct()
        _setupSelections.Verify(r => r.AddRangeAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 3. Unknown setup option key returns 400-style failure Result (not an exception)
    [Fact]
    public async Task Handle_UnknownSetupOptionKey_Returns400Failure()
    {
        var options = new List<string>
        {
            TenantSetupOptionKeys.JobFamilyCreation,
            "invalid_option_key"
        };
        var command = CommandWith(new TenantConfigurationSetupInfo(options));

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("invalid_option_key");
        _setupSelections.Verify(r => r.AddRangeAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // 4. Setup selections do NOT affect plan modules or Owner role permissions
    [Fact]
    public async Task Handle_SetupOptions_DoNotAffectOwnerRolePermissions()
    {
        // Role seeder is called with plan modules (from IModuleEntitlementService / plan),
        // not from setup options. Verify roleSeeder is called once regardless of setup options,
        // with the modules from the plan — not from setup options.
        var options = new List<string>
        {
            TenantSetupOptionKeys.RolePermissionConfiguration
        };
        var command = CommandWith(new TenantConfigurationSetupInfo(options));

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // RoleSeeder receives modules derived from the plan (plan.GetIncludedModules()), not setup options
        _roleSeeder.Verify(r => r.SeedOwnerRoleAsync(
            It.IsAny<Guid>(),
            It.Is<IReadOnlyList<string>>(modules => !modules.Contains(TenantSetupOptionKeys.RolePermissionConfiguration)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 5. Null/empty TenantConfigurationSetup is accepted (no rows created)
    [Fact]
    public async Task Handle_NullTenantConfigurationSetup_SucceedsWithNoSelections()
    {
        var command = CommandWith(null);

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _setupSelections.Verify(r => r.AddRangeAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptySetupOptions_SucceedsWithNoSelections()
    {
        var command = CommandWith(new TenantConfigurationSetupInfo([]));

        var result = await BuildHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _setupSelections.Verify(r => r.AddRangeAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
