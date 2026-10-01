using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class WorkStatusTemplateChangeTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CallerEmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static TaskStatusChangeRequest Request(Guid projectId, Guid requestedBy, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, RequestedByEmployeeId = requestedBy,
        Status = TaskStatusChangeRequestStatuses.Pending, CreatedAt = createdAt,
    };

    private static TaskStatusChangeAccess Access(bool canEditDirectly) =>
        new(new Objective { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Root" }, canEditDirectly, !canEditDirectly);

    [Fact]
    public async Task No_caller_employee_returns_empty_ok_summary()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        var source = new WorkStatusTemplateChangeTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object,
            Mock.Of<ITaskStatusChangeRequestRepository>(), Mock.Of<ITaskStatusChangeAccessService>(), Mock.Of<IProjectRepository>());

        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(0, summary.PendingCount);
    }

    [Fact]
    public async Task Resolves_access_once_per_distinct_project_not_once_per_row_and_filters_to_canEditDirectly()
    {
        var currentUser = CurrentUser();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(CallerEmployeeId);

        var approverProjectId = Guid.NewGuid();
        var requesterOnlyProjectId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        var requests = new Mock<ITaskStatusChangeRequestRepository>();
        requests.Setup(x => x.ListAllPendingAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Request(approverProjectId, requesterId, DateTimeOffset.Parse("2026-09-01T00:00:00Z")),
            Request(approverProjectId, requesterId, DateTimeOffset.Parse("2026-08-01T00:00:00Z")),
            Request(requesterOnlyProjectId, requesterId, DateTimeOffset.Parse("2026-07-01T00:00:00Z")),
        ]);

        var access = new Mock<ITaskStatusChangeAccessService>();
        access.Setup(x => x.ResolveAsync(TenantId, approverProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Access(canEditDirectly: true));
        access.Setup(x => x.ResolveAsync(TenantId, requesterOnlyProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Access(canEditDirectly: false));

        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [requesterId] = "Arjun M" });
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Project { Id = approverProjectId, TenantId = TenantId, Name = "Core Platform" }]);

        var source = new WorkStatusTemplateChangeTeamActionSource(
            currentUser, Mock.Of<IModuleEntitlementService>(), identity.Object, requests.Object, access.Object, projects.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        // Only the two requests from the project the caller can edit directly are counted; the
        // third project's request (where the caller can only file a request, not decide) is excluded.
        Assert.Equal(2, summary.PendingCount);
        Assert.All(summary.TopItems, item => Assert.Contains("Core Platform", item.Title));
        access.Verify(x => x.ResolveAsync(TenantId, approverProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        access.Verify(x => x.ResolveAsync(TenantId, requesterOnlyProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return currentUser.Object;
    }
}
