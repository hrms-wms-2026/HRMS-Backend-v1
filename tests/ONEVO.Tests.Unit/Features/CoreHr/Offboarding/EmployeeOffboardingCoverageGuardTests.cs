using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeOffboardingCoverageGuardTests
{
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actingUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private EmployeeOffboardingCoverageGuard CreateSut() =>
        new(_scopeResolver.Object, _employeeRepository.Object, _positionAssignmentRepository.Object);

    [Fact]
    public async Task EnsureCovered_DepartmentInScope_ReturnsNull()
    {
        var departmentId = Guid.NewGuid();
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, DepartmentId = departmentId });
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _actingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid> { departmentId }, new HashSet<Guid>()));

        var result = await CreateSut().EnsureCovered(_tenantId, _actingUserId, _employeeId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EnsureCovered_NoOverlap_ReturnsForbidden()
    {
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, DepartmentId = Guid.NewGuid(), LegalEntityId = Guid.NewGuid() });
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _actingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));

        var result = await CreateSut().EnsureCovered(_tenantId, _actingUserId, _employeeId);

        // CanViewAllTenantEmployees = true is deliberately ignored - still Forbidden.
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(403);
    }
}
