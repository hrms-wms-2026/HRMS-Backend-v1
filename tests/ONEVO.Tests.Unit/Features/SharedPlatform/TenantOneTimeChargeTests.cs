using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

public class TenantOneTimeChargeTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000001");
    private static readonly Guid ChargeId = Guid.Parse("bbbbbbbb-0001-0001-0001-000000000001");
    private static readonly string ValidKey = TenantSetupOptionKeys.JobFamilyCreation;

    private readonly Mock<ITenantOneTimeChargeRepository> _repo = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public TenantOneTimeChargeTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // Default: tenant exists
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Tenant { Id = TenantId, Name = "Test Tenant" });
    }

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOneTimeCharge_ValidKey_CreatesChargeWithCorrectFields()
    {
        _repo.Setup(r => r.HasActiveChargeAsync(TenantId, ValidKey, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        _repo.Setup(r => r.AddAsync(It.IsAny<TenantOneTimeCharge>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var handler = new CreateOneTimeChargeCommandHandler(_repo.Object, _tenants.Object, _unitOfWork.Object, _currentUser.Object, _clock.Object);
        var cmd = new CreateOneTimeChargeCommand(TenantId, ValidKey, "Setup fee", 250m, "USD");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(TenantId);
        result.Value.SetupOptionKey.Should().Be(ValidKey);
        result.Value.Amount.Should().Be(250m);
        result.Value.Currency.Should().Be("USD");
        result.Value.Status.Should().Be("draft");
        result.Value.ChargedOnce.Should().BeTrue();

        // Ensure subscription recurring fields are NOT touched (they live on TenantSubscription — never in scope here)
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOneTimeCharge_UnknownSetupOptionKey_Returns400()
    {
        var handler = new CreateOneTimeChargeCommandHandler(_repo.Object, _tenants.Object, _unitOfWork.Object, _currentUser.Object, _clock.Object);
        var cmd = new CreateOneTimeChargeCommand(TenantId, "unknown_key", "Bad fee", 100m, "USD");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOneTimeCharge_DuplicateNonVoidCharge_Returns409()
    {
        _repo.Setup(r => r.HasActiveChargeAsync(TenantId, ValidKey, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var handler = new CreateOneTimeChargeCommandHandler(_repo.Object, _tenants.Object, _unitOfWork.Object, _currentUser.Object, _clock.Object);
        var cmd = new CreateOneTimeChargeCommand(TenantId, ValidKey, "Dup fee", 100m, "USD");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOneTimeCharge_PaidCharge_Returns400()
    {
        var charge = new TenantOneTimeCharge
        {
            Id = ChargeId, TenantId = TenantId, SetupOptionKey = ValidKey,
            Description = "Paid charge", Amount = 150m, Currency = "USD",
            Status = "paid", ChargedOnce = true
        };
        _repo.Setup(r => r.GetByIdAsync(ChargeId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(charge);

        var handler = new UpdateOneTimeChargeCommandHandler(_repo.Object, _unitOfWork.Object);
        var cmd = new UpdateOneTimeChargeCommand(TenantId, ChargeId, 200m, null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateOneTimeCharge_VoidCharge_Returns400()
    {
        var charge = new TenantOneTimeCharge
        {
            Id = ChargeId, TenantId = TenantId, SetupOptionKey = ValidKey,
            Description = "Void charge", Amount = 150m, Currency = "USD",
            Status = "void", ChargedOnce = true
        };
        _repo.Setup(r => r.GetByIdAsync(ChargeId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(charge);

        var handler = new UpdateOneTimeChargeCommandHandler(_repo.Object, _unitOfWork.Object);
        var cmd = new UpdateOneTimeChargeCommand(TenantId, ChargeId, 200m, null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Get ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTenantOneTimeCharges_ReturnsChargesForTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var charges = new List<TenantOneTimeCharge>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, SetupOptionKey = ValidKey,
                    Description = "Fee A", Amount = 100m, Currency = "USD", Status = "draft",
                    ChargedOnce = true, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, SetupOptionKey = TenantSetupOptionKeys.EmployeeInvite,
                    Description = "Fee B", Amount = 50m, Currency = "USD", Status = "approved",
                    ChargedOnce = true, CreatedAt = now.AddMinutes(-5) }
        };
        _repo.Setup(r => r.GetByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(charges);

        var handler = new GetTenantOneTimeChargesQueryHandler(_repo.Object);
        var query = new GetTenantOneTimeChargesQuery(TenantId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(d => d.TenantId.Should().Be(TenantId));
    }
}
