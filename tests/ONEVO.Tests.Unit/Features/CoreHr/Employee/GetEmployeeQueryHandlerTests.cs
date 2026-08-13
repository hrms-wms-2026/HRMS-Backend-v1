using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
    }

    private GetEmployeeQueryHandler CreateHandler() =>
        new(_employeeRepository.Object, _scopeResolver.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenEmployeeDoesNotExistInTenant()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenEmployeeExistsButIsOutsideVisibilityScope()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsEmployee_WhenVisible()
    {
        var response = new EmployeeListItemResponse(_employeeId, "E-001", "Ada Lovelace", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null);
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }
}
