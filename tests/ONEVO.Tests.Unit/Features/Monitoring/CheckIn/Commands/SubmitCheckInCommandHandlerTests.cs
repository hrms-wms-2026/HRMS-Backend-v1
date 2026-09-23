using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class SubmitCheckInCommandHandlerTests
{
    private readonly Mock<ICheckInRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<CoreEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IExpectedWorkAreaResolver> _expectedWorkAreas = new();
    private readonly Mock<IEmployeeWorkLocationRepository> _workLocations = new();
    private readonly Mock<ILocationChangeRequestRepository> _locationChangeRequests = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    public SubmitCheckInCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.LegalEntityId).Returns(_legalEntityId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });

        _clock.SetupGet(x => x.UtcNow).Returns(_now);

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, _legalEntityId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var employee = new Employee { Id = _employeeId, TenantId = _tenantId, LegalEntityId = _legalEntityId, UserId = _userId };
        _employees.Setup(e => e.GetByUserAndLegalEntityAsync(_tenantId, _userId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "Asia/Colombo" });

        _workLocations.Setup(w => w.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeWorkLocation?)null);
        _locationChangeRequests.Setup(l => l.GetActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationChangeRequest?)null);
    }

    private SubmitCheckInCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _switcher.Object, _toggles.Object, _clock.Object,
        _unitOfWork.Object, _employees.Object, _legalEntities.Object, _expectedWorkAreas.Object,
        _workLocations.Object, _locationChangeRequests.Object);

    private void SetWorkAreaResolution(bool selfRegistersLocation, bool allowsDailyLocationChoice) =>
        _expectedWorkAreas.Setup(r => r.ResolveAsync(
                It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(new ExpectedWorkAreaResolution(
                Guid.NewGuid(), "Custom Mode", "Asia/Colombo", ExpectedWorkAreaResolver.SourceActiveWorkMode,
                selfRegistersLocation, allowsDailyLocationChoice)));

    private static SubmitCheckInCommand ValidCommand() => new(6.9271, 79.8612, 10, "Colombo", "SERIAL-1");

    [Fact]
    public async Task SelfRegistersLocation_NoExistingLocation_RegistersReferencePoint()
    {
        SetWorkAreaResolution(selfRegistersLocation: true, allowsDailyLocationChoice: false);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _workLocations.Verify(w => w.AddAsync(
            It.Is<EmployeeWorkLocation>(l => l.EmployeeId == _employeeId && l.Latitude == 6.9271 && l.Longitude == 79.8612),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SelfRegistersLocation_ExistingLocationAlready_DoesNotOverwrite()
    {
        SetWorkAreaResolution(selfRegistersLocation: true, allowsDailyLocationChoice: false);
        _workLocations.Setup(w => w.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeWorkLocation
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Latitude = 1, Longitude = 1, RegisteredAt = _now.AddDays(-10), UpdatedAt = _now.AddDays(-10)
            });

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllowsDailyLocationChoice_DoesNotSelfRegister_DailyConfirmationFlowOwnsIt()
    {
        SetWorkAreaResolution(selfRegistersLocation: false, allowsDailyLocationChoice: true);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FixedOfficeWorkMode_NeitherFlagSet_DoesNotRegisterLocation()
    {
        SetWorkAreaResolution(selfRegistersLocation: false, allowsDailyLocationChoice: false);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LocationTrackingDisabled_LocationNotPersistedOnCheckIn()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, _legalEntityId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Latitude);
        Assert.Null(result.Value.Longitude);
        _workLocations.Verify(w => w.AddAsync(It.IsAny<EmployeeWorkLocation>(), It.IsAny<CancellationToken>()), Times.Never);
        _expectedWorkAreas.Verify(r => r.ResolveAsync(
            It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The re-prompt is about updating an EXISTING registered point, not about whether this
    /// check-in itself auto-registers one - it must fire independently of the work mode's own
    /// location behavior (a fixed office-checked employee could still be mid-transition off an
    /// older self-registering mode with a pending approved change).
    /// </summary>
    [Fact]
    public async Task ApprovedLocationChangeRequest_RePromptsRegardlessOfWorkModeFlags()
    {
        SetWorkAreaResolution(selfRegistersLocation: false, allowsDailyLocationChoice: false);
        var approvedRequestId = Guid.NewGuid();
        _locationChangeRequests.Setup(l => l.GetActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationChangeRequest
            {
                Id = approvedRequestId, TenantId = _tenantId, EmployeeId = _employeeId,
                Status = LocationChangeRequest.StatusApproved
            });

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(approvedRequestId, result.Value!.PendingLocationChangeRequestId);
    }
}
