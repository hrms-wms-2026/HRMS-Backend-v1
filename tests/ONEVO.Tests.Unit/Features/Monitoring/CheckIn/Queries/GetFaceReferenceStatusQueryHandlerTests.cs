using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetFaceReferenceStatus;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Queries;

public class GetFaceReferenceStatusQueryHandlerTests
{
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IBiometricProfileRepository> _profiles = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetFaceReferenceStatusQueryHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private GetFaceReferenceStatusQueryHandler CreateSut() => new(
        _device.Object, _tenants.Object, _tenantSwitcher.Object, _profiles.Object, _employeeIdentity.Object);

    private void SetupProfile(BiometricProfile? profile) =>
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

    [Fact]
    public async Task NoProfile_NotEnrolled()
    {
        SetupProfile(null);

        var result = await CreateSut().Handle(new GetFaceReferenceStatusQuery(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
        result.Value.ReferencePhotoCount.Should().Be(0);
    }

    [Fact]
    public async Task ThreePhotoProfile_EnrolledWithThree()
    {
        SetupProfile(new BiometricProfile
        {
            TenantId = _tenantId, EmployeeId = _employeeId, Status = BiometricProfileStatus.Enrolled,
            ReferencePhotoFileId = Guid.NewGuid(), LeftReferencePhotoFileId = Guid.NewGuid(), RightReferencePhotoFileId = Guid.NewGuid()
        });

        var result = await CreateSut().Handle(new GetFaceReferenceStatusQuery(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeTrue();
        result.Value.ReferencePhotoCount.Should().Be(3);
    }

    [Fact]
    public async Task FailedProfile_NotEnrolled()
    {
        SetupProfile(new BiometricProfile
        {
            TenantId = _tenantId, EmployeeId = _employeeId, Status = BiometricProfileStatus.Failed,
            ReferencePhotoFileId = Guid.NewGuid()
        });

        var result = await CreateSut().Handle(new GetFaceReferenceStatusQuery(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
    }

    [Fact]
    public async Task ProfileWithoutPhoto_NotEnrolled()
    {
        SetupProfile(new BiometricProfile
        {
            TenantId = _tenantId, EmployeeId = _employeeId, Status = BiometricProfileStatus.Enrolled
        });

        var result = await CreateSut().Handle(new GetFaceReferenceStatusQuery(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetFaceReferenceStatusQuery(), CancellationToken.None);

        result.StatusCode.Should().Be(401);
    }
}
