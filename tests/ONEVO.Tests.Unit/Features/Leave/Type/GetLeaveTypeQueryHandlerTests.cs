using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class GetLeaveTypeQueryHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetLeaveTypeQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_ExistingType_ReturnsMappedResponse()
    {
        var leaveTypeId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, leaveTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType
            {
                Id = leaveTypeId,
                TenantId = _tenantId,
                Name = "Annual Leave",
                Code = "ANNUAL",
                IsPaid = true,
                IsActive = true
            });

        var handler = new GetLeaveTypeQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeaveTypeQuery(leaveTypeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(leaveTypeId, result.Value!.Id);
        Assert.Equal("Annual Leave", result.Value.Name);
        Assert.Equal("ANNUAL", result.Value.Code);
    }

    [Fact]
    public async Task Handle_MissingType_ReturnsNotFound()
    {
        var leaveTypeId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, leaveTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveType?)null);

        var handler = new GetLeaveTypeQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeaveTypeQuery(leaveTypeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(false);
        var handler = new GetLeaveTypeQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeaveTypeQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _repoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
