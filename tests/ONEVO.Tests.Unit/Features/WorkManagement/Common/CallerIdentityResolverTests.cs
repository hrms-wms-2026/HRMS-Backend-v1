using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Common;

public class CallerIdentityResolverTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly CallerIdentityResolver _sut;

    public CallerIdentityResolverTests()
    {
        _sut = new CallerIdentityResolver(_employees.Object);
    }

    [Fact]
    public async Task ResolveCallerEmployeeIdAsync_EmployeeExists_ReturnsEmployeeId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId };
        _employees.Setup(e => e.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var result = await _sut.ResolveCallerEmployeeIdAsync(tenantId, userId, CancellationToken.None);

        Assert.Equal(employee.Id, result);
    }

    [Fact]
    public async Task ResolveCallerEmployeeIdAsync_NoEmployeeRecord_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _employees.Setup(e => e.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await _sut.ResolveCallerEmployeeIdAsync(tenantId, userId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveDisplayNamesByEmployeeIdAsync_ExistingEmployees_ReturnsNameForEachId()
    {
        var tenantId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        _employees.Setup(e => e.GetByIdAsync(tenantId, employeeId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId1, TenantId = tenantId, FirstName = "Ada", LastName = "Lovelace" });
        _employees.Setup(e => e.GetByIdAsync(tenantId, employeeId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId2, TenantId = tenantId, FirstName = "Grace", LastName = "Hopper" });

        var result = await _sut.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [employeeId1, employeeId2], CancellationToken.None);

        Assert.Equal("Ada Lovelace", result[employeeId1]);
        Assert.Equal("Grace Hopper", result[employeeId2]);
    }

    [Fact]
    public async Task ResolveDisplayNamesByEmployeeIdAsync_MissingEmployee_OmittedFromResult()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _employees.Setup(e => e.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await _sut.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [employeeId], CancellationToken.None);

        Assert.False(result.ContainsKey(employeeId));
    }
}
