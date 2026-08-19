using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyObjectiveInvitationsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsCallersPendingInvitations()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(CallerUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, CallerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForEmployeeAsync(TenantId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(), InvitedEmployeeId = CallerEmployeeId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetMyObjectiveInvitationsQueryHandler(currentUser.Object, identity.Object, invitations.Object);

        var result = await handler.Handle(new GetMyObjectiveInvitationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(CallerEmployeeId, result.Value![0].InvitedEmployeeId);
    }
}
