using Moq;
using ONEVO.Api.Controllers.Tenant.Monitoring.ActivityMonitoring;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyFocusStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

public sealed class WellbeingWidgetDataProviderTests
{
    private static WellbeingWidgetDataProvider Create(WidgetProviderTestKit kit) =>
        new(kit.Mediator.Object, kit.User.Object, kit.Entitlements.Object);

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        // MonitoringActivityController.GetMyFocusStatus is gated by [RequirePermission("activity:read:self")].
        var kit = new WidgetProviderTestKit(permissions: ["activity:read"]);

        var result = await Create(kit).GetAsync(default);

        WidgetProviderTestKit.AssertForbidden(result);
        kit.Mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_same_view_model_as_endpoint()
    {
        var kit = new WidgetProviderTestKit(permissions: ["activity:read:self"]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyFocusStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FocusStatusResponse>.Success(new FocusStatusResponse(true, 95)));

        var provided = await Create(kit).GetAsync(default);
        var endpoint = await new MonitoringActivityController(kit.Mediator.Object).GetMyFocusStatus(default);

        WidgetProviderTestKit.AssertSameBody(endpoint, provided);
        kit.Mediator.Verify(m => m.Send(It.IsAny<GetMyFocusStatusQuery>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
