using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCurrentEmployee;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetCurrentEmployeeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static GetCurrentEmployeeQueryHandler BuildHandler(
        bool authenticated = true, bool employeeExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? EmployeeId : null);

        return new GetCurrentEmployeeQueryHandler(currentUser.Object, identity.Object);
    }

    [Fact]
    public async Task Handle_AuthenticatedWithEmployeeRecord_ReturnsCallerEmployeeId()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetCurrentEmployeeQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.EmployeeId);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var handler = BuildHandler(authenticated: false);

        var result = await handler.Handle(new GetCurrentEmployeeQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var handler = BuildHandler(employeeExists: false);

        var result = await handler.Handle(new GetCurrentEmployeeQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
