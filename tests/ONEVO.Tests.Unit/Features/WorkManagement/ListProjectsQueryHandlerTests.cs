using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
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

    private (ListProjectsQueryHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
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

        var handler = new ListProjectsQueryHandler(currentUser.Object, projects.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_NullTargetUserId_ResolvesToCallersOwnId()
    {
        var (handler, projects) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitTargetUserId_UsesItInsteadOfCaller()
    {
        var (handler, projects) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, OtherUserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IsLead_ComputedAgainstTargetUserIdNotCaller()
    {
        var (handler, _) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.Value!.Items.Single().IsLead);
    }

    [Fact]
    public async Task Handle_ReturnsPagingMetadataFromRepository()
    {
        var (handler, _) = BuildHandler([MakeProject(UserId)], total: 47);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 2, PageSize = 10 }), CancellationToken.None);

        Assert.Equal(47, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(10, result.Value.PageSize);
    }

    [Fact]
    public async Task Handle_NonPositivePageNumber_ClampedToOne()
    {
        var (handler, projects) = BuildHandler([], 0);

        await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 0 }), CancellationToken.None);

        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, 0, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
