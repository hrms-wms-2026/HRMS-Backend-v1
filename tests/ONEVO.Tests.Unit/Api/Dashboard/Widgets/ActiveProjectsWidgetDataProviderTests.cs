using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class ActiveProjectsWidgetDataProviderTests
{
    private static ActiveProjectsWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        // ProjectsController is gated by [RequireAnyModule(<Work Management modules>)].
        var kit = new WidgetProviderTestKit(permissions: ["activity:read:self"], activeModules: ["leave"]);

        var result = await Create(kit).GetAsync(default);

        WidgetProviderTestKit.AssertForbidden(result);
        kit.Mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(activeModules: ["worksync_foundation"]);
        var item = new ProjectListItemResponse(
            Guid.NewGuid(), "Website", "WEB", Guid.NewGuid(), "desc", Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "#6366f1", true,
            120m, 40.5m, false, false, null, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null,
            [new LabelSummaryDto(Guid.NewGuid(), "Q4", "#22c55e")],
            [new ProjectMemberAvatarDto(Guid.NewGuid(), "Ada Lovelace", null)], 3);
        kit.Mediator.Setup(m => m.Send(It.IsAny<ListProjectsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<ProjectListItemResponse>>.Success(
                new PagedResult<ProjectListItemResponse>([item], 1, 100, 1)));

        var provided = await Create(kit).GetAsync(default);
        // active-projects-card calls ProjectApiService.listMine(1, 100) -> ?pageNumber=1&pageSize=100.
        var endpoint = await new ProjectsController(kit.Mediator.Object)
            .ListMine(new PagedRequest { PageNumber = 1, PageSize = 100 }, default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(
                It.Is<ListProjectsQuery>(q => q.TargetEmployeeId == null
                    && q.Paging.PageNumber == 1 && q.Paging.PageSize == 100
                    && q.Paging.SortBy == null && q.Paging.SortDirection == "asc"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
