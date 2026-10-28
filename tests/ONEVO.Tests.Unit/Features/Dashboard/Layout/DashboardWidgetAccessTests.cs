using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class DashboardWidgetAccessTests
{
    private static MyTeamCapabilitiesResponse Caps(
        bool available = false, bool status = false, bool people = false,
        bool exceptions = false, bool work = false, bool leads = false) =>
        new(available, status, people, exceptions, work, leads, false, null);

    private static (DashboardWidgetAccess Sut, Mock<IMediator> Mediator) Create(
        Func<Task<Result<MyTeamCapabilitiesResponse>>> reply)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetMyTeamCapabilitiesQuery>(), It.IsAny<CancellationToken>()))
            .Returns(reply);
        return (new DashboardWidgetAccess(mediator.Object, NullLogger<DashboardWidgetAccess>.Instance), mediator);
    }

    private static DashboardWidgetAccess With(MyTeamCapabilitiesResponse caps) =>
        Create(() => Task.FromResult(Result<MyTeamCapabilitiesResponse>.Success(caps))).Sut;

    private static readonly string[] AllMyWorkExceptActionCenter =
        DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork)
            .Select(d => d.Key).Where(k => k != "action-center").ToArray();

    [Fact]
    public async Task Plain_employee_gets_my_work_without_action_center()
    {
        var keys = await With(Caps()).GetAllowedKeysAsync(DashboardKeys.MyWork, default);

        Assert.DoesNotContain("action-center", keys);
        Assert.True(keys.SetEquals(AllMyWorkExceptActionCenter));
    }

    [Fact]
    public async Task Lead_gets_action_center_and_team_progress()
    {
        var sut = With(Caps(available: true, leads: true));

        var work = await sut.GetAllowedKeysAsync(DashboardKeys.MyWork, default);
        var team = await sut.GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.Contains("action-center", work);
        Assert.Contains("team-progress", team);
        Assert.DoesNotContain("team-status", team);
        Assert.DoesNotContain("team-attendance", team);
    }

    [Fact]
    public async Task Approval_only_viewer_gets_action_center_but_no_team_status_or_progress()
    {
        var sut = With(Caps(available: false, people: true));

        var work = await sut.GetAllowedKeysAsync(DashboardKeys.MyWork, default);
        var team = await sut.GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.Contains("action-center", work);
        Assert.DoesNotContain("team-status", team);
        Assert.DoesNotContain("team-progress", team);
    }

    [Fact]
    public async Task Team_status_requires_availability_and_people_status()
    {
        var team = await With(Caps(available: true, status: true))
            .GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.Contains("team-status", team);
        Assert.Contains("team-attendance", team);
        Assert.Contains("team-quick-actions", team);
    }

    [Fact]
    public async Task Team_quick_actions_always_allowed()
    {
        var team = await With(Caps()).GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.Equal(["team-quick-actions"], team.ToArray());
    }

    [Fact]
    public async Task Capabilities_failure_hides_gated_widgets()
    {
        var (sut, _) = Create(() => Task.FromResult(Result<MyTeamCapabilitiesResponse>.Failure("boom")));

        var work = await sut.GetAllowedKeysAsync(DashboardKeys.MyWork, default);
        var team = await sut.GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.DoesNotContain("action-center", work);
        Assert.Equal(["team-quick-actions"], team.ToArray());
    }

    [Fact]
    public async Task Capabilities_exception_hides_gated_widgets_without_throwing()
    {
        var (sut, _) = Create(() => throw new InvalidOperationException("db down"));

        var team = await sut.GetAllowedKeysAsync(DashboardKeys.MyTeam, default);

        Assert.Equal(["team-quick-actions"], team.ToArray());
        Assert.True(await sut.CapabilitiesUnavailableAsync(default));
    }

    [Fact]
    public async Task Capabilities_failure_is_reported_as_unavailable()
    {
        var (failed, _) = Create(() => Task.FromResult(Result<MyTeamCapabilitiesResponse>.Failure("boom")));
        Assert.True(await failed.CapabilitiesUnavailableAsync(default));

        Assert.False(await With(Caps()).CapabilitiesUnavailableAsync(default));
    }

    [Fact]
    public async Task Capabilities_query_runs_once_per_request()
    {
        var (sut, mediator) = Create(() => Task.FromResult(Result<MyTeamCapabilitiesResponse>.Success(Caps(leads: true))));

        await sut.GetAllowedKeysAsync(DashboardKeys.MyWork, default);
        await sut.GetAllowedKeysAsync(DashboardKeys.MyTeam, default);
        await sut.CapabilitiesUnavailableAsync(default);

        mediator.Verify(
            m => m.Send(It.IsAny<GetMyTeamCapabilitiesQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
