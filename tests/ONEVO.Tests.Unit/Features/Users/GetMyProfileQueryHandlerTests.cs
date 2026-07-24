using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Users.Queries.GetMyProfile;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Tests.Unit.Features.Users;

public class GetMyProfileQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUserProfileRepository> _repo = new();

    private GetMyProfileQueryHandler CreateHandler() =>
        new(_currentUser.Object, _repo.Object);

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenNotAuthenticated()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsProfile_WithNullEmployee_WhenNoEmployeeRecord()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(userId);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _currentUser.Setup(u => u.Email).Returns("test@example.com");

        _repo.Setup(r => r.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Employee);
        Assert.Empty(result.Value.Devices);
        Assert.Null(result.Value.WorkLocation);
        Assert.Null(result.Value.FaceScan);
        Assert.Equal("test@example.com", result.Value.Email);
    }

    [Fact]
    public async Task Handle_ReturnsDevices_WhenEmployeeHasAgents()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(userId);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _currentUser.Setup(u => u.Email).Returns("emp@example.com");

        var employee = new Employee
        {
            Id = employeeId,
            UserId = userId,
            TenantId = tenantId,
            EmployeeNumber = "EMP001",
            FirstName = "John",
            LastName = "Doe",
            Email = "emp@example.com",
            HireDate = new DateOnly(2024, 1, 1),
            EmploymentStatusId = 1,
            WorkModeId = 1
        };

        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DeviceId = "device-001",
            DeviceName = "JOHN-LAPTOP",
            OsVersion = "Windows 11 23H2",
            AgentVersion = "1.0.0",
            Status = "active",
            LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        _repo.Setup(r => r.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _repo.Setup(r => r.GetAgentsByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RegisteredAgent> { agent });
        _repo.Setup(r => r.GetWorkLocationSettingsAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeWorkLocationSettings?)null);
        _repo.Setup(r => r.GetActiveReferencePhotoAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerificationReferencePhoto?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Devices);
        Assert.Equal("JOHN-LAPTOP", result.Value.Devices.ElementAt(0).DeviceName);
        Assert.Equal("active", result.Value.Devices.ElementAt(0).Status);
    }

    [Fact]
    public async Task Handle_ReturnsWorkLocationAndFaceScan_WhenBothExist()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(userId);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _currentUser.Setup(u => u.Email).Returns("emp2@example.com");

        var employee = new Employee
        {
            Id = employeeId,
            UserId = userId,
            TenantId = tenantId,
            EmployeeNumber = "EMP002",
            FirstName = "Jane",
            LastName = "Smith",
            Email = "emp2@example.com",
            HireDate = new DateOnly(2024, 3, 15),
            EmploymentStatusId = 1,
            WorkModeId = 1
        };

        var settings = new EmployeeWorkLocationSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            WorkMode = "hybrid",
            WorkLocationVerificationEnabled = true,
            GracePeriodMinutes = 10,
            PhotoChallengeOnMismatch = true,
            SetById = Guid.NewGuid()
        };

        var photo = new VerificationReferencePhoto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            PhotoFileId = Guid.NewGuid(),
            Source = "hr_verified_profile",
            Status = "approved",
            CapturedAt = DateTimeOffset.UtcNow.AddDays(-7),
            IsActive = true
        };

        _repo.Setup(r => r.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _repo.Setup(r => r.GetAgentsByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RegisteredAgent>());
        _repo.Setup(r => r.GetWorkLocationSettingsAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _repo.Setup(r => r.GetActiveReferencePhotoAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(photo);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hybrid", result.Value!.WorkLocation!.WorkMode);
        Assert.True(result.Value.WorkLocation.VerificationEnabled);
        Assert.Equal(10, result.Value.WorkLocation.GracePeriodMinutes);
        Assert.Equal("approved", result.Value.FaceScan!.Status);
        Assert.Equal("hr_verified_profile", result.Value.FaceScan.Source);
        Assert.True(result.Value.FaceScan.IsActive);
    }
}
