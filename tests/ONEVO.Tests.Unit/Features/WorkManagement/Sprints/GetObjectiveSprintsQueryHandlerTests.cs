using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class GetObjectiveSprintsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private static Objective Objective(Guid id, Guid? parentId) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ParentObjectiveId = parentId,
        Title = "Obj",
        OwnerId = Guid.NewGuid(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Sprint SprintOnObjective() => new()
    {
        Id = SprintId,
        TenantId = TenantId,
        ProjectId = ProjectId,
        ObjectiveId = ObjectiveId,
        Name = "Sprint 1",
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 14),
        Status = SprintStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private GetObjectiveSprintsQueryHandler BuildHandler(
        Objective? objective,
        Objective? parent = null,
        bool hasReadPermission = false,
        Func<IReadOnlyList<Guid>, bool>? membershipForIds = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective);
        if (parent is not null)
            objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, parent.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(parent);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(
                TenantId, ProjectId, CallerEmployeeId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                membershipForIds?.Invoke(ids) ?? false);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sprint> { SprintOnObjective() });

        return new GetObjectiveSprintsQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object,
            permissionResolver.Object, sprints.Object);
    }

    [Fact]
    public async Task Handle_ActiveMembershipOnObjective_ReturnsSprints()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            membershipForIds: ids => ids.Contains(ObjectiveId));

        var result = await handler.Handle(new GetObjectiveSprintsQuery(ObjectiveId, ActiveOnly: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sprint = Assert.Single(result.Value!);
        Assert.Equal(SprintId, sprint.Id);
        Assert.Equal(ObjectiveId, sprint.ObjectiveId);
    }

    [Fact]
    public async Task Handle_ActiveMembershipOnlyOnAncestor_ReturnsSprints()
    {
        var parent = Objective(ParentId, parentId: null);
        IReadOnlyList<Guid>? walkedIds = null;
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: ParentId),
            parent,
            membershipForIds: ids =>
            {
                walkedIds = ids;
                return ids.Contains(ParentId);
            });

        var result = await handler.Handle(new GetObjectiveSprintsQuery(ObjectiveId, ActiveOnly: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.NotNull(walkedIds);
        Assert.Contains(ObjectiveId, walkedIds);
        Assert.Contains(ParentId, walkedIds);
    }

    [Fact]
    public async Task Handle_ProjectsReadPermissionWithoutMembership_ReturnsSprints()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            hasReadPermission: true,
            membershipForIds: _ => false);

        var result = await handler.Handle(new GetObjectiveSprintsQuery(ObjectiveId, ActiveOnly: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Handle_NoMembershipAndNoPermission_ReturnsForbidden()
    {
        var handler = BuildHandler(
            Objective(ObjectiveId, parentId: null),
            membershipForIds: _ => false);

        var result = await handler.Handle(new GetObjectiveSprintsQuery(ObjectiveId, ActiveOnly: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnknownObjective_ReturnsNotFound()
    {
        var handler = BuildHandler(objective: null);

        var result = await handler.Handle(new GetObjectiveSprintsQuery(ObjectiveId, ActiveOnly: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
