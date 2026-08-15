using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveMembersQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid InvitedEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadEmployeeId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private GetObjectiveMembersQueryHandler BuildHandler(
        Objective? objective, Guid? callerId = null, List<string>? permissions = null, bool hasMembership = true)
    {
        var resolvedCallerId = callerId ?? HeadUserId;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, HeadUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeadEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = HeadEmployeeId, IsActive = true, JoinedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, EmployeeId = MemberEmployeeId, IsActive = true, JoinedAt = DateTimeOffset.UtcNow }
            });
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasMembership);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, InvitedEmployeeId = InvitedEmployeeId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow }
            });

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions ?? ["projects:read"]);

        return new GetObjectiveMembersQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object, invitations.Object, permissionResolver.Object);
    }

    [Fact]
    public async Task Handle_ReturnsRealMembersAndPendingInvitationsMerged()
    {
        var handler = BuildHandler(SubObjective());

        var result = await handler.Handle(new GetObjectiveMembersQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.EmployeeId == HeadEmployeeId && i.IsHead && !i.Pending);
        Assert.Contains(result.Value.Items, i => i.EmployeeId == MemberEmployeeId && !i.IsHead && !i.Pending);
        Assert.Contains(result.Value.Items, i => i.EmployeeId == InvitedEmployeeId && i.Pending && i.InviteType == ProjectInvitationTypes.Member);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var handler = BuildHandler(null);

        var result = await handler.Handle(new GetObjectiveMembersQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHeadNotMemberNoReadAll_ReturnsForbidden()
    {
        var handler = BuildHandler(SubObjective(), callerId: OtherUserId, permissions: [], hasMembership: false);

        var result = await handler.Handle(new GetObjectiveMembersQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
