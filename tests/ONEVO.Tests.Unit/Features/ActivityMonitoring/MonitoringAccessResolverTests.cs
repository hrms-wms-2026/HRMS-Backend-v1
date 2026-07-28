using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Services.ActivityMonitoring;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

public sealed class MonitoringAccessResolverTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();

    [Fact]
    public async Task OwnEmployee_IsAllowed()
    {
        var employee = CreateEmployee();
        ConfigureCurrentEmployee(employee);

        Assert.True(await CreateResolver().CanReadEmployeeAsync(employee.Id));
    }

    [Fact]
    public async Task DirectReport_IsAllowed()
    {
        var manager = CreateEmployee();
        var report = CreateEmployee(manager.TenantId);
        report.ManagerId = manager.Id;
        ConfigureCurrentEmployee(manager);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                report.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        Assert.True(await CreateResolver().CanReadEmployeeAsync(report.Id));
    }

    [Fact]
    public async Task MonitoringConfigurator_IsAllowedForTenantEmployee()
    {
        var administrator = CreateEmployee();
        var target = CreateEmployee(administrator.TenantId);
        ConfigureCurrentEmployee(administrator);
        _currentUser.Setup(user => user.HasPermission("monitoring:configure"))
            .Returns(true);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                target.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Assert.True(await CreateResolver().CanReadEmployeeAsync(target.Id));
    }

    [Fact]
    public async Task UnrelatedEmployee_IsDenied()
    {
        var employee = CreateEmployee();
        var target = CreateEmployee(employee.TenantId);
        ConfigureCurrentEmployee(employee);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                target.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Assert.False(await CreateResolver().CanReadEmployeeAsync(target.Id));
    }

    [Fact]
    public async Task CrossTenantEmployee_IsDenied()
    {
        var employee = CreateEmployee();
        var target = CreateEmployee();
        ConfigureCurrentEmployee(employee);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                target.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Assert.False(await CreateResolver().CanReadEmployeeAsync(target.Id));
    }

    private MonitoringAccessResolver CreateResolver() =>
        new(_currentUser.Object, _profiles.Object);

    private void ConfigureCurrentEmployee(Employee employee)
    {
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(user => user.UserId).Returns(employee.UserId);
        _currentUser.SetupGet(user => user.TenantId).Returns(employee.TenantId);
        _profiles.Setup(repository => repository.GetEmployeeByUserIdAsync(
                employee.UserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
    }

    private static Employee CreateEmployee(Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId ?? Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        EmployeeNumber = Guid.NewGuid().ToString("N"),
        FirstName = "Employee",
        Email = $"{Guid.NewGuid():N}@example.test",
        HireDate = new DateOnly(2026, 1, 1)
    };
}
