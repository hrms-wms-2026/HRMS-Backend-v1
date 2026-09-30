using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeOverviewAccessTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public EmployeeOverviewAccessTests()
    {
        _user.SetupGet(u => u.UserId).Returns(_userId);
    }

    [Fact]
    public async Task HasAccess_True_WhenCallerHoldsThePermission_WithoutLookingUpTheCaller()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.True(result);
        _employees.Verify(e => e.GetDefaultForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HasAccess_True_WhenTheCallerIsViewingTheirOwnRecord()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_False_WhenNeitherPermissionNorSelf()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccess_False_WhenTheCallerHasNoEmployeeRecord()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.False(result);
    }
}
