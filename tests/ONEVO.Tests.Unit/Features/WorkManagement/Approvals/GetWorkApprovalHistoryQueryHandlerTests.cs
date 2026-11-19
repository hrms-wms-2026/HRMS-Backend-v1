using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetWorkApprovalHistory;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public sealed class GetWorkApprovalHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSentAndReceivedItemsWithResolvedActors()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(
                tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [employeeId] = "Alex Silva",
                [requesterId] = "Sam Lee"
            });

        var history = new Mock<IWorkApprovalHistoryRepository>();
        history.Setup(x => x.ListForEmployeeAsync(
                tenantId, projectId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalHistoryRecord>
            {
                new(
                    Guid.NewGuid(), objectiveId, "task_creation", "approved", "Payments module",
                    "{\"title\":\"Add audit events\"}", requesterId, employeeId, employeeId, null,
                    DateTimeOffset.Parse("2026-09-23T08:00:00Z"),
                    DateTimeOffset.Parse("2026-09-23T09:00:00Z")),
                new(
                    Guid.NewGuid(), objectiveId, "allocation_extend", "pending", "Payments module",
                    "{\"requestedAdditionalHours\":8,\"reason\":\"Finish integration\"}", employeeId,
                    requesterId, null, null, DateTimeOffset.Parse("2026-09-24T08:00:00Z"), null)
            });

        var handler = new GetWorkApprovalHistoryQueryHandler(
            currentUser.Object, identity.Object, history.Object);

        var result = await handler.Handle(new GetWorkApprovalHistoryQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("received", result.Value[0].Direction);
        Assert.Equal("Sam Lee", result.Value[0].RequestedByName);
        Assert.Equal("Alex Silva", result.Value[0].DecidedByName);
        Assert.Equal("sent", result.Value[1].Direction);
        Assert.Contains("8 additional hours", result.Value[1].Detail);
    }
}
