using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class ListLeavePoliciesQueryHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListLeavePoliciesQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_Authenticated_ReturnsMappedPolicies()
    {
        var policy = new LeavePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "LK Policy",
            Country = "LK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        _repoMock.Setup(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyAggregate(policy, [], [], [])]);

        var handler = new ListLeavePoliciesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeavePoliciesQuery(false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("LK Policy", result.Value![0].Name);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(false);
        var handler = new ListLeavePoliciesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeavePoliciesQuery(false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
