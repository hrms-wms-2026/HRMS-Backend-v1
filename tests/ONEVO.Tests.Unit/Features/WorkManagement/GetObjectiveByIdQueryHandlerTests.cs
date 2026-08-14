using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective Target(bool isActive = true, Guid? ownerId = null) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsActive = isActive,
        Title = "Sub", OwnerId = ownerId ?? Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective Parent() => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = null, IsDefault = true, IsActive = true,
        Title = "Default", OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members) BuildHandler(
        Objective? target, List<string> permissions, bool hasAncestorOrSelfMembership,
        Guid? callerId = null, IReadOnlyDictionary<Guid, string>? names = null)
    {
        var resolvedCallerId = callerId ?? UserId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, resolvedCallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(names ?? new Dictionary<Guid, string>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(Parent());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAncestorOrSelfMembership);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var handler = new GetObjectiveByIdQueryHandler(currentUser.Object, identity.Object, objectives.Object, members.Object, permissionResolver.Object);
        return (handler, members);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members) = BuildHandler(Target(), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButAncestorOrSelfMembership_Succeeds()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_MembershipCheckIncludesTargetAndAncestorIds()
    {
        var (handler, members) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, EmployeeId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(ObjectiveId) && ids.Contains(ParentId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNoMembership_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveObjective_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Target(isActive: false), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ResolvesOwnerAndReportingManagerNames()
    {
        var ownerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var target = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsActive = true,
            Title = "Sub", OwnerId = ownerId, ReportingManagerId = managerId,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
        };
        var names = new Dictionary<Guid, string>
        {
            [ownerId] = "Jane Doe",
            [managerId] = "John Smith"
        };
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false, names: names);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane Doe", result.Value!.OwnerName);
        Assert.Equal("John Smith", result.Value.ReportingManagerName);
    }

    [Fact]
    public async Task Handle_IsOwnerTrue_WhenCallerIsTheOwner()
    {
        var target = Target(ownerId: EmployeeId);
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.IsOwner);
    }

    [Fact]
    public async Task Handle_IsOwnerFalse_WhenCallerIsNotTheOwner()
    {
        var target = Target();
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.Value!.IsOwner);
    }
}
