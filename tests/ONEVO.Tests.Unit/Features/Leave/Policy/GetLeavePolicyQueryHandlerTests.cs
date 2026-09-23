using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class GetLeavePolicyQueryHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _policyId = Guid.NewGuid();

    public GetLeavePolicyQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_Found_ReturnsMappedPolicy()
    {
        var policy = new LeavePolicy
        {
            Id = _policyId,
            TenantId = _tenantId,
            Name = "LK Policy",
            Country = "LK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicyAggregate(policy, [], [], []));

        var handler = new GetLeavePolicyQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeavePolicyQuery(_policyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("LK Policy", result.Value!.Name);
    }

    [Fact]
    public async Task Handle_Missing_Returns404()
    {
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeavePolicyAggregate?)null);
        var handler = new GetLeavePolicyQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeavePolicyQuery(_policyId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
