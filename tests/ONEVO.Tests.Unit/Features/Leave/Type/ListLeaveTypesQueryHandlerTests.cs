using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class ListLeaveTypesQueryHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListLeaveTypesQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyActiveByDefault()
    {
        _repoMock.Setup(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveType> { new() { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Annual", Code = "ANNUAL", IsActive = true } });

        var handler = new ListLeaveTypesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeaveTypesQuery(IncludeInactive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        _repoMock.Verify(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeInactive_WithoutLeaveRead_ReturnsOnlyActive()
    {
        // A self-service employee (leave:read-own) lists types for the New Request wizard only;
        // deactivated types are HR configuration, not something they can request.
        _currentUserMock.Setup(c => c.HasPermission("leave:read")).Returns(false);
        _repoMock.Setup(r => r.ListAsync(_tenantId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveType>());

        var handler = new ListLeaveTypesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeaveTypesQuery(IncludeInactive: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeInactive_WithLeaveRead_ReturnsInactiveToo()
    {
        _currentUserMock.Setup(c => c.HasPermission("leave:read")).Returns(true);
        _repoMock.Setup(r => r.ListAsync(_tenantId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveType>());

        var handler = new ListLeaveTypesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        await handler.Handle(new ListLeaveTypesQuery(IncludeInactive: true), CancellationToken.None);

        _repoMock.Verify(r => r.ListAsync(_tenantId, true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
