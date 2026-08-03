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
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public class CreateTenantCommandHandlerTests
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
        _invitationService.Object);

    private static CreateTenantCommand BaseCommand(TenantOwnerInviteRequest? ownerInvite = null) =>
        new("Acme Corp", "acme-corp", "office_it", "51-200",
            "Acme Legal Ltd", "REG123", "LK", "Asia/Colombo", "LKR",
            new SubscriptionInfo(PlanId, "monthly", "subscription"),
            ownerInvite);

    public CreateTenantCommandHandlerTests()
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
    }

    [Fact]
    public async Task Handle_WithoutOwnerInvite_ReturnsNextStepProvisioning()
    {
        var result = await BuildHandler().Handle(BaseCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NextStep.Should().Be("provisioning");
        result.Value.OwnerInvite.Should().BeNull();
        _invitationService.Verify(s => s.CreateInviteRecordsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<TenantOwnerInviteRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _invitationService.Verify(s => s.QueueInviteEmailAsync(
                It.IsAny<PendingOwnerInvite>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithOwnerInvite_CreatesRecordsThenQueuesEmailThenSaves()
    {
        var ownerInvite = new TenantOwnerInviteRequest("owner@acme.com", "John", "Doe", null, null, null);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(72);
        var pending = new PendingOwnerInvite(
            UserId: Guid.NewGuid(),
            Email: "owner@acme.com",
            FullName: "John Doe",
            PlaintextToken: "tok",
            ExpiresAt: expiresAt,
            TenantId: Guid.NewGuid(),
            TenantName: "Acme Corp",
            FirstName: "John");

        var sequence = new MockSequence();
        _invitationService.InSequence(sequence)
            .Setup(s => s.CreateInviteRecordsAsync(
                It.IsAny<Guid>(),
                OwnerRoleId,
                It.IsAny<TenantOwnerInviteRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PendingOwnerInvite>.Success(pending));
        _invitationService.InSequence(sequence)
            .Setup(s => s.QueueInviteEmailAsync(
                It.Is<PendingOwnerInvite>(p => p.UserId == pending.UserId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await BuildHandler().Handle(BaseCommand(ownerInvite), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NextStep.Should().Be("owner_invite");
        result.Value.OwnerInvite.Should().NotBeNull();
        result.Value.OwnerInvite!.UserId.Should().Be(pending.UserId);
        result.Value.OwnerInvite.InviteExpiresAt.Should().Be(expiresAt);
        result.Value.OwnerInvite.DeliveryStatus.Should().Be("queued");

        _invitationService.Verify(s => s.CreateInviteRecordsAsync(
                It.IsAny<Guid>(),
                OwnerRoleId,
                It.IsAny<TenantOwnerInviteRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // The email outbox message must be queued BEFORE the single commit so both
        // persist in one transaction.
        _invitationService.Verify(
            s => s.QueueInviteEmailAsync(
                It.Is<PendingOwnerInvite>(p => p.UserId == pending.UserId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithOwnerInvite_RecordCreationFailure_AbortsBeforeSave()
    {
        var ownerInvite = new TenantOwnerInviteRequest("owner@acme.com", "John", "Doe", null, null, null);
        _invitationService
            .Setup(s => s.CreateInviteRecordsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<TenantOwnerInviteRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PendingOwnerInvite>.Conflict("Active invite already exists."));

        var result = await BuildHandler().Handle(BaseCommand(ownerInvite), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _invitationService.Verify(s => s.QueueInviteEmailAsync(
                It.IsAny<PendingOwnerInvite>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PlanNotFound_ReturnsNotFound()
    {
        _plans.Setup(p => p.GetByIdAsync(PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPlan?)null);

        var result = await BuildHandler().Handle(BaseCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
