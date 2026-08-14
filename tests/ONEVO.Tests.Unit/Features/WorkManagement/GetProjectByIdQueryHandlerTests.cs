using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetProjectByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LeadId = Guid.NewGuid();

    private static Project Project(bool isActive = true) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = LeadId, IsActive = isActive,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private static Mock<IEntityAssetRepository> BuildEmptyEntityAssets()
    {
        var entityAssets = new Mock<IEntityAssetRepository>();
        entityAssets.Setup(x => x.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.IsAny<IReadOnlyCollection<Guid>>(), "project_cover", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
        return entityAssets;
    }

    private static Mock<ILabelRepository> BuildEmptyLabels()
    {
        var labels = new Mock<ILabelRepository>();
        labels.Setup(x => x.GetByProjectIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Label>>());
        return labels;
    }

    private static void SetupEmptyMemberLists(Mock<IProjectMemberRepository> members)
    {
        members.Setup(x => x.ListDistinctActiveMemberUserIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
    }

    private static Mock<IEmployeeRepository> BuildEmptyEmployees()
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee>());
        return employees;
    }

    private (GetProjectByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members, Mock<IEntityAssetRepository> EntityAssets, Mock<ILabelRepository> Labels, Mock<IEmployeeRepository> Employees) BuildHandler(
        Project? project, List<string> permissions, bool isActiveMember)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isActiveMember);
        SetupEmptyMemberLists(members);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var entityAssets = BuildEmptyEntityAssets();
        var labels = BuildEmptyLabels();
        var employees = BuildEmptyEmployees();

        var handler = new GetProjectByIdQueryHandler(
            currentUser.Object, projects.Object, members.Object, permissionResolver.Object, entityAssets.Object, labels.Object, employees.Object);
        return (handler, members, entityAssets, labels, employees);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members, _, _, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButActiveMember_Succeeds()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), [], isActiveMember: true);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNotMember_ReturnsForbidden()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), [], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WildcardPermission_Succeeds()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), ["*"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(isActive: false), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LeadCaller_IsLeadTrue()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(LeadId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(Project());

        var members = new Mock<IProjectMemberRepository>();
        SetupEmptyMemberLists(members);
        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(LeadId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(["projects:read"]);
        var entityAssets = BuildEmptyEntityAssets();
        var labels = BuildEmptyLabels();
        var employees = BuildEmptyEmployees();

        var handler = new GetProjectByIdQueryHandler(
            currentUser.Object, projects.Object, members.Object, permissionResolver.Object, entityAssets.Object, labels.Object, employees.Object);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsLead);
    }

    [Fact]
    public async Task Handle_ProjectHasAPrimaryCoverAsset_AttachesItsFileIdAsLogoFileId()
    {
        var fileId = Guid.NewGuid();
        var (handler, _, entityAssets, _, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);
        entityAssets.Setup(x => x.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ProjectId)), "project_cover", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [ProjectId] = fileId });

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.Equal(fileId, result.Value!.LogoFileId);
    }

    [Fact]
    public async Task Handle_ProjectHasNoCoverAsset_LogoFileIdIsNull()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.Null(result.Value!.LogoFileId);
    }

    [Fact]
    public async Task Handle_ProjectHasLabels_AttachesThemAsSummaries()
    {
        var (handler, _, _, labels, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);
        var label = new Label { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Personal", Color = "#8B5CF6" };
        labels.Setup(x => x.GetByProjectIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ProjectId)), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Label>> { [ProjectId] = [label] });

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        var tag = result.Value!.Labels.Single();
        Assert.Equal("Personal", tag.Name);
        Assert.Equal("#8B5CF6", tag.Color);
    }

    [Fact]
    public async Task Handle_ProjectHasNoLabels_LabelsIsEmpty()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.Empty(result.Value!.Labels);
    }

    [Fact]
    public async Task Handle_ProjectHasActiveMembers_AttachesResolvedDisplayNamesAndCount()
    {
        var memberUserId = Guid.NewGuid();
        var (handler, members, _, _, employees) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);
        members.Setup(x => x.ListDistinctActiveMemberUserIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ProjectId)), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>> { [ProjectId] = [memberUserId] });
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ProjectId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [ProjectId] = 3 });
        employees.Setup(x => x.GetByUserIdsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(memberUserId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = memberUserId, FirstName = "Diya", LastName = "Perera", Email = "diya@test.com", EmployeeNumber = "E2" } });

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        var member = result.Value!.Members.Single();
        Assert.Equal(memberUserId, member.UserId);
        Assert.Equal("Diya Perera", member.DisplayName);
        Assert.Equal(3, result.Value.MemberCount);
    }

    [Fact]
    public async Task Handle_ProjectHasNoActiveMembers_MembersIsEmptyAndCountIsZero()
    {
        var (handler, _, _, _, _) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.Empty(result.Value!.Members);
        Assert.Equal(0, result.Value.MemberCount);
    }
}
