using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.WorkLocation;

public class ConfirmWorkLocationCommandHandlerTests
{
    private readonly Mock<IDailyWorkLocationConfirmationRepository> _confirmations = new();
    private readonly Mock<IEmployeeWorkLocationRepository> _workLocations = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public ConfirmWorkLocationCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_employeeId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });
        _clock.UtcNow = new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);
    }

    private ConfirmWorkLocationCommandHandler MakeHandler() => new(
        _device.Object, _confirmations.Object, _workLocations.Object,
        _tenants.Object, _switcher.Object, _clock, _uow);

    [Fact]
    public async Task Handle_FirstHomeConfirmationWithFix_RegistersEmployeeWorkLocation()
    {
        _workLocations.Setup(w => w.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeWorkLocation?)null);

        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("home", 6.9271, 79.8612, 15), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _workLocations.Verify(w => w.AddAsync(
            It.Is<EmployeeWorkLocation>(l => l.EmployeeId == _employeeId && l.Latitude == 6.9271 && l.Longitude == 79.8612),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HomeConfirmation_ExistingReferenceAlreadyRegistered_DoesNotOverwrite()
    {
        _workLocations.Setup(w => w.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeWorkLocation
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Latitude = 1, Longitude = 1, RegisteredAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow
            });

        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("home", 6.9271, 79.8612, 15), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OfficeConfirmation_NeverRegistersWorkLocation()
    {
        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("office", 6.9271, 79.8612, 15), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoGpsFix_StoresConfirmationWithNullCoordinatesAndSkipsRegistration()
    {
        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("home", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _confirmations.Verify(c => c.UpsertAsync(
            It.Is<DailyWorkLocationConfirmation>(x => x.Latitude == null && x.Longitude == null),
            It.IsAny<CancellationToken>()), Times.Once);
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnsupportedLocationType_ReturnsFailure()
    {
        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("space_station", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await MakeHandler().Handle(
            new ConfirmWorkLocationCommand("office", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
