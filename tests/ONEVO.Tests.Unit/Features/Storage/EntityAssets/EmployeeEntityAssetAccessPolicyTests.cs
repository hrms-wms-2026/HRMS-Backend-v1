using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class EmployeeEntityAssetAccessPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public async Task CanReadAsync_EmployeeExistsInTenant_ReturnsTrue()
    {
        var employees = new Mock<CommonEmployeeRepo>();
        employees.Setup(r => r.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = EmployeeId, TenantId = TenantId });
        var sut = new EmployeeEntityAssetAccessPolicy(employees.Object);

        var result = await sut.CanReadAsync(TenantId, EmployeeId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadAsync_EmployeeNotFoundInTenant_ReturnsFalse()
    {
        var employees = new Mock<CommonEmployeeRepo>();
        employees.Setup(r => r.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);
        var sut = new EmployeeEntityAssetAccessPolicy(employees.Object);

        var result = await sut.CanReadAsync(TenantId, EmployeeId, CancellationToken.None);

        result.Should().BeFalse();
    }
}
