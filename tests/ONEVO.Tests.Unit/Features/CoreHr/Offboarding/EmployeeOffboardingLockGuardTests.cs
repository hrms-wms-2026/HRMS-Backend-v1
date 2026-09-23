using FluentAssertions;
using Moq;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeOffboardingLockGuardTests
{
    [Theory]
    [InlineData(EmploymentStatusIds.Resigned)]
    [InlineData(EmploymentStatusIds.Terminated)]
    public async Task EnsureMutable_ResignedOrTerminated_ReturnsConflict(int statusId)
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var repo = new Mock<IEmployeeRepository>();
        repo.Setup(r => r.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, EmploymentStatusId = statusId });

        var result = await new EmployeeOffboardingLockGuard(repo.Object).EnsureMutable(tenantId, employeeId);

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task EnsureMutable_ActiveEmployee_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var repo = new Mock<IEmployeeRepository>();
        repo.Setup(r => r.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, EmploymentStatusId = EmploymentStatusIds.Active });

        var result = await new EmployeeOffboardingLockGuard(repo.Object).EnsureMutable(tenantId, employeeId);

        result.Should().BeNull();
    }
}
