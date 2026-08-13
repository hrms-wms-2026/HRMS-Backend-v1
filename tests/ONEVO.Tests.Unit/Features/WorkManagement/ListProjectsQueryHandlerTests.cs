using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListProjectsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static Project MakeProject(Guid leadId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, LeadId = leadId, IsActive = true,
        Name = "P", Identifier = "P" + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow
    };

    private (ListProjectsQueryHandler Handler, Mock<IProjectRepository> Projects, Mock<IEntityAssetRepository> EntityAssets, Mock<ILabelRepository> Labels, Mock<IProjectMemberRepository> Members, Mock<IEmployeeRepository> Employees) BuildHandler(
        IReadOnlyList<Project> items, int total)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.ListForMemberAsync(
                TenantId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, total));

        var entityAssets = new Mock<IEntityAssetRepository>();
        entityAssets.Setup(x => x.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.IsAny<IReadOnlyCollection<Guid>>(), "project_cover", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var labels = new Mock<ILabelRepository>();
        labels.Setup(x => x.GetByProjectIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Label>>());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListDistinctActiveMemberUserIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee>());

        var handler = new ListProjectsQueryHandler(currentUser.Object, projects.Object, entityAssets.Object, labels.Object, members.Object, employees.Object);
        return (handler, projects, entityAssets, labels, members, employees);
    }

    [Fact]
    public async Task Handle_NullTargetUserId_ResolvesToCallersOwnId()
    {
        var (handler, projects, _, _, _, _) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitTargetUserId_UsesItInsteadOfCaller()
    {
        var (handler, projects, _, _, _, _) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, OtherUserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IsLead_ComputedAgainstTargetUserIdNotCaller()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.Value!.Items.Single().IsLead);
    }

    [Fact]
    public async Task Handle_ReturnsPagingMetadataFromRepository()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(UserId)], total: 47);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 2, PageSize = 10 }), CancellationToken.None);

        Assert.Equal(47, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(10, result.Value.PageSize);
    }

    [Fact]
    public async Task Handle_NonPositivePageNumber_ClampedToOne()
    {
        var (handler, projects, _, _, _, _) = BuildHandler([], 0);

        await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 0 }), CancellationToken.None);

        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, 0, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProjectHasAPrimaryCoverAsset_AttachesItsFileIdAsLogoFileId()
    {
        var project = MakeProject(UserId);
        var fileId = Guid.NewGuid();
        var (handler, _, entityAssets, _, _, _) = BuildHandler([project], 1);
        entityAssets.Setup(x => x.GetPrimaryFileIdsByOwnerAsync(
                TenantId, "project", It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), "project_cover", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [project.Id] = fileId });

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.Equal(fileId, result.Value!.Items.Single().LogoFileId);
    }

    [Fact]
    public async Task Handle_ProjectHasNoCoverAsset_LogoFileIdIsNull()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.Null(result.Value!.Items.Single().LogoFileId);
    }

    [Fact]
    public async Task Handle_ForwardsDescriptionAndUpdatedAtFromTheEntity()
    {
        var project = MakeProject(UserId);
        project.Description = "Rebuild the marketing site";
        project.UpdatedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var (handler, _, _, _, _, _) = BuildHandler([project], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var item = result.Value!.Items.Single();
        Assert.Equal("Rebuild the marketing site", item.Description);
        Assert.Equal(project.UpdatedAt, item.UpdatedAt);
    }

    [Fact]
    public async Task Handle_ProjectHasLabels_AttachesThemAsSummaries()
    {
        var project = MakeProject(UserId);
        var (handler, _, _, labels, _, _) = BuildHandler([project], 1);
        var label = new Label { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = project.Id, Name = "Marketing", Color = "#F59E0B" };
        labels.Setup(x => x.GetByProjectIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Label>> { [project.Id] = [label] });

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var tag = result.Value!.Items.Single().Labels.Single();
        Assert.Equal("Marketing", tag.Name);
        Assert.Equal("#F59E0B", tag.Color);
    }

    [Fact]
    public async Task Handle_ProjectHasNoLabels_LabelsIsEmpty()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.Empty(result.Value!.Items.Single().Labels);
    }

    [Fact]
    public async Task Handle_ProjectHasActiveMembers_AttachesResolvedDisplayNamesAndCount()
    {
        var project = MakeProject(UserId);
        var memberUserId = Guid.NewGuid();
        var (handler, _, _, _, members, employees) = BuildHandler([project], 1);
        members.Setup(x => x.ListDistinctActiveMemberUserIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>> { [project.Id] = [memberUserId] });
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [project.Id] = 7 });
        employees.Setup(x => x.GetByUserIdsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(memberUserId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = memberUserId, FirstName = "Arun", LastName = "Kumar", Email = "arun@test.com", EmployeeNumber = "E1" } });

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var item = result.Value!.Items.Single();
        var member = item.Members.Single();
        Assert.Equal(memberUserId, member.UserId);
        Assert.Equal("Arun Kumar", member.DisplayName);
        Assert.Equal(7, item.MemberCount);
    }

    [Fact]
    public async Task Handle_ProjectHasNoActiveMembers_MembersIsEmptyAndCountIsZero()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var item = result.Value!.Items.Single();
        Assert.Empty(item.Members);
        Assert.Equal(0, item.MemberCount);
    }
}
