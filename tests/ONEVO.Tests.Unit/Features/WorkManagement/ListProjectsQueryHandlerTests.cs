using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListProjectsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private static Project MakeProject(Guid leadId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, LeadId = leadId, IsActive = true,
        Name = "P", Identifier = "P" + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow
    };

    private (ListProjectsQueryHandler Handler, Mock<IProjectRepository> Projects, Mock<IEntityAssetRepository> EntityAssets, Mock<ILabelRepository> Labels, Mock<IProjectMemberRepository> Members, Mock<ICallerIdentityResolver> Identity) BuildHandler(
        IReadOnlyList<Project> items, int total)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

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
        members.Setup(x => x.ListDistinctActiveMemberEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var handler = new ListProjectsQueryHandler(currentUser.Object, identity.Object, projects.Object, entityAssets.Object, labels.Object, members.Object);
        return (handler, projects, entityAssets, labels, members, identity);
    }

    [Fact]
    public async Task Handle_NullTargetEmployeeId_ResolvesToCallersOwnId()
    {
        var (handler, projects, _, _, _, _) = BuildHandler([MakeProject(EmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, EmployeeId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitTargetEmployeeId_UsesItInsteadOfCaller()
    {
        var (handler, projects, _, _, _, _) = BuildHandler([MakeProject(OtherEmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherEmployeeId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, OtherEmployeeId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IsLead_ComputedAgainstTargetEmployeeIdNotCaller()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(OtherEmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherEmployeeId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.Value!.Items.Single().IsLead);
    }

    [Fact]
    public async Task Handle_ReturnsPagingMetadataFromRepository()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(EmployeeId)], total: 47);

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

        projects.Verify(x => x.ListForMemberAsync(TenantId, EmployeeId, 0, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProjectHasAPrimaryCoverAsset_AttachesItsFileIdAsLogoFileId()
    {
        var project = MakeProject(EmployeeId);
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
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(EmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.Null(result.Value!.Items.Single().LogoFileId);
    }

    [Fact]
    public async Task Handle_ForwardsDescriptionAndUpdatedAtFromTheEntity()
    {
        var project = MakeProject(EmployeeId);
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
        var project = MakeProject(EmployeeId);
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
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(EmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.Empty(result.Value!.Items.Single().Labels);
    }

    [Fact]
    public async Task Handle_ProjectHasActiveMembers_AttachesResolvedDisplayNamesAndCount()
    {
        var project = MakeProject(EmployeeId);
        var memberEmployeeId = Guid.NewGuid();
        var (handler, _, _, _, members, identity) = BuildHandler([project], 1);
        members.Setup(x => x.ListDistinctActiveMemberEmployeeIdsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>> { [project.Id] = [memberEmployeeId] });
        members.Setup(x => x.CountDistinctActiveMembersAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(project.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [project.Id] = 7 });
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(memberEmployeeId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [memberEmployeeId] = "Arun Kumar" });

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var item = result.Value!.Items.Single();
        var member = item.Members.Single();
        Assert.Equal(memberEmployeeId, member.UserId);
        Assert.Equal("Arun Kumar", member.DisplayName);
        Assert.Equal(7, item.MemberCount);
    }

    [Fact]
    public async Task Handle_ProjectHasNoActiveMembers_MembersIsEmptyAndCountIsZero()
    {
        var (handler, _, _, _, _, _) = BuildHandler([MakeProject(EmployeeId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        var item = result.Value!.Items.Single();
        Assert.Empty(item.Members);
        Assert.Equal(0, item.MemberCount);
    }
}
