using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class GetBiometricProfileQueryHandlerTests
{
    private readonly Mock<IBiometricRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<IEmployeeIdentityResolver> _employeeResolver = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetBiometricProfileQueryHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });
    }

    private GetBiometricProfileQueryHandler CreateSut() => new(
        _repository.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _employeeResolver.Object);

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Unknown_tenant_returns_401()
    {
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task When_employee_cannot_be_resolved_returns_404()
    {
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task When_no_active_profile_returns_404()
    {
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
        _repository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeBiometricProfile?)null);

        var result = await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task When_active_profile_exists_returns_profile_dto()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var profile = new EmployeeBiometricProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            Status = BiometricProfileStatus.Active,
            CreatedAt = createdAt
        };

        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
        _repository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProfileId.Should().Be(profile.Id);
        result.Value.Status.Should().Be(BiometricProfileStatus.Active);
        result.Value.EnrolledAt.Should().Be(createdAt);
    }

    [Fact]
    public async Task Tenant_context_is_switched_before_resolving_profile()
    {
        var switchedBeforeResolve = false;

        _switcher.Setup(s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == _tenantId),
                It.IsAny<CancellationToken>()))
            .Callback(() => switchedBeforeResolve = true)
            .Returns(Task.CompletedTask);

        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .Callback(() => switchedBeforeResolve.Should().BeTrue())
            .ReturnsAsync(_employeeId);
        _repository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeBiometricProfile?)null);

        await CreateSut().Handle(new GetBiometricProfileQuery(), CancellationToken.None);

        _switcher.Verify(s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == _tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
