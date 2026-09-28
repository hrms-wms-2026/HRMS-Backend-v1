using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeIdentity;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeIdentityQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeIdentityQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
    }

    private GetEmployeeIdentityQueryHandler CreateHandler() => new(_currentUser.Object, _identity.Object);

    [Fact]
    public async Task Handle_ReturnsNameAndAvatar_ForAnyEmployeeInTenant_RegardlessOfCallerPermissions()
    {
        // No employees:read/employees:write stub, no visibility-scope resolver at all - this
        // handler's whole point is not needing either.
        _identity
            .Setup(r => r.ResolveIdentitiesByEmployeeIdAsync(_tenantId, new[] { _employeeId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>
            {
                [_employeeId] = new EmployeeIdentityDto("Nevi Peiris", Guid.NewGuid())
            });

        var result = await CreateHandler().Handle(new GetEmployeeIdentityQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_employeeId, result.Value!.Id);
        Assert.Equal("Nevi Peiris", result.Value!.Name);
        Assert.NotNull(result.Value!.AvatarFileId);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenEmployeeDoesNotExistInTenant()
    {
        _identity
            .Setup(r => r.ResolveIdentitiesByEmployeeIdAsync(_tenantId, new[] { _employeeId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentityDto>());

        var result = await CreateHandler().Handle(new GetEmployeeIdentityQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
