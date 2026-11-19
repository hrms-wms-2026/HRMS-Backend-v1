using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Notifications;

public class ListProjectWorkNotificationsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task ReturnsCallerRowsForProject_PagedBy50_WithActorNameAndLabel()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Me);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Actor] = "Bala" });
        var logs = new Mock<IWorkNotificationLogRepository>();
        logs.Setup(x => x.ListForRecipientAsync(TenantId, ProjectId, Me, 50, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkNotificationLog>
            {
                new() { Id = Guid.NewGuid(), ActorEmployeeId = Actor, Kind = WorkNotificationKinds.Direct,
                        ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task, TargetTitle = "Audit" }
            });

        var handler = new ListProjectWorkNotificationsQueryHandler(currentUser.Object, identity.Object, logs.Object);
        var result = await handler.Handle(new ListProjectWorkNotificationsQuery(ProjectId, 2), default);

        result.Value.Should().ContainSingle();
        result.Value![0].ActorName.Should().Be("Bala");
        result.Value[0].ActionLabel.Should().Be("edited the task");
    }
}
