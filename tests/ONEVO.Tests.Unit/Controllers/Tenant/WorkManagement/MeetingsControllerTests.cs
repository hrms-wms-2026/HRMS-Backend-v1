using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CancelMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.EditMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.WorkManagement;

/// <summary>/api/v1/meetings maps every route onto its command or query, and every failure onto ProblemDetails with
/// the stable `code` extension (spec 7).</summary>
public sealed class MeetingsControllerTests
{
    private static readonly Guid Series = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMediator> _mediator = new();
    private MeetingsController Controller => new(_mediator.Object);

    [Fact]
    public async Task Create_Returns201WithTheOutcome()
    {
        var created = new MeetingCreatedResponse("created", Guid.NewGuid(), Guid.NewGuid(), null, []);
        _mediator.Setup(m => m.Send(It.IsAny<CreateMeetingCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingCreatedResponse>.Success(created));

        var result = Assert.IsType<ObjectResult>(await Controller.Create(new CreateMeetingRequest(null, null, "x", null, null, false,
            Start, 30, false, "UTC", null, [], [], null), CancellationToken.None));

        Assert.Equal((201, created), (result.StatusCode, result.Value));
    }

    [Fact]
    public async Task AFailure_IsProblemDetailsWithItsCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<EditMeetingCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingEditResponse>.Failure("Someone else changed this meeting.", 409, MeetingErrorCodes.Changed));

        var result = Assert.IsType<ObjectResult>(await Controller.Edit(Series, Start,
            new EditMeetingRequest(MeetingScopes.This, 0, "x", null, null, null, null, null, null, null, null), CancellationToken.None));

        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal((409, 409, MeetingErrorCodes.Changed), (result.StatusCode, problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task Edit_PassesTheSeriesAndOriginalStartThrough()
    {
        EditMeetingCommand? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<EditMeetingCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<MeetingEditResponse>> c, CancellationToken _) => sent = (EditMeetingCommand)c)
            .ReturnsAsync(Result<MeetingEditResponse>.Success(new MeetingEditResponse(Series, Guid.NewGuid(), false)));

        Assert.IsType<OkObjectResult>(await Controller.Edit(Series, Start,
            new EditMeetingRequest(MeetingScopes.All, 2, "x", null, null, null, null, null, null, null, null), CancellationToken.None));

        Assert.Equal((Series, Start, MeetingScopes.All), (sent!.SeriesId, sent.OriginalStart, sent.Edit.Scope));
    }

    [Fact]
    public async Task Cancel_IsNoContent_AndAnInvisibleMeetingIs404WithItsCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CancelMeetingCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _mediator.Setup(m => m.Send(It.IsAny<GetMeetingOccurrenceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingOccurrenceResponse>.Failure("Meeting not found.", 404, MeetingErrorCodes.NotFound));

        Assert.IsType<NoContentResult>(await Controller.Cancel(Series, Start, new CancelMeetingRequest(MeetingScopes.This), CancellationToken.None));
        var notFound = Assert.IsType<ObjectResult>(await Controller.GetOccurrence(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal((404, MeetingErrorCodes.NotFound), (notFound.StatusCode, ((ProblemDetails)notFound.Value!).Extensions["code"]));
    }

    [Fact]
    public async Task Respond_And_GuestRemoval_MapToTheirCommands()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RespondToMeetingCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<bool>.Success(true));
        RemoveSeriesGuestCommand? removed = null;
        _mediator.Setup(m => m.Send(It.IsAny<RemoveSeriesGuestCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<bool>> c, CancellationToken _) => removed = (RemoveSeriesGuestCommand)c)
            .ReturnsAsync(Result<bool>.Success(true));

        Assert.IsType<NoContentResult>(await Controller.Respond(Series, new RespondToMeetingRequest("accepted", null, null), CancellationToken.None));
        Assert.IsType<NoContentResult>(await Controller.RemoveSeriesGuest(Series, "client@example.com", CancellationToken.None));
        Assert.Equal((Series, "client@example.com"), (removed!.SeriesId, removed.Email));
    }

    private static MeetingLinkResponse Link(bool releasePending = false)
        => new(Guid.NewGuid(), "microsoft_teams", "active", "https://teams.example/j", null, null, 1, releasePending);

    [Fact]
    public async Task GetLink_And_CreateLink_MapToTheirRequests()
    {
        var view = new MeetingSeriesLinkResponse("https://teams.example/j", null);
        _mediator.Setup(m => m.Send(It.Is<GetMeetingLinkQuery>(q => q.SeriesId == Series), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingSeriesLinkResponse>.Success(view));
        CreateMeetingLinkCommand? sent = null;
        var link = Link();
        _mediator.Setup(m => m.Send(It.IsAny<CreateMeetingLinkCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<MeetingLinkResponse>> c, CancellationToken _) => sent = (CreateMeetingLinkCommand)c)
            .ReturnsAsync(Result<MeetingLinkResponse>.Success(link));

        var got = Assert.IsType<OkObjectResult>(await Controller.GetLink(Series, CancellationToken.None));
        var created = Assert.IsType<OkObjectResult>(await Controller.CreateLink(Series,
            new MeetingLinkRequest("microsoft_teams", Regenerate: true), CancellationToken.None));

        Assert.Equal((view, link), (got.Value, created.Value));
        Assert.Equal((Series, "microsoft_teams", true), (sent!.SeriesId, sent.Request.Platform, sent.Request.Regenerate));
    }

    [Fact]
    public async Task RetryLink_And_CleanupConfirmed_MapToTheirCommands()
    {
        var link = Link();
        _mediator.Setup(m => m.Send(It.Is<RetryMeetingLinkCommand>(c => c.SeriesId == Series), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingLinkResponse>.Success(link));
        _mediator.Setup(m => m.Send(It.Is<ConfirmMeetingLinkCleanupCommand>(c => c.SeriesId == Series), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("The link is not unresolved.", 409, MeetingErrorCodes.LinkNotUnresolved));

        var retried = Assert.IsType<OkObjectResult>(await Controller.RetryLink(Series, CancellationToken.None));
        var refused = Assert.IsType<ObjectResult>(await Controller.ConfirmLinkCleanup(Series, CancellationToken.None));

        Assert.Equal(link, retried.Value);
        Assert.Equal((409, MeetingErrorCodes.LinkNotUnresolved), (refused.StatusCode, ((ProblemDetails)refused.Value!).Extensions["code"]));
    }

    [Fact]
    public async Task RemoveLink_IsOkWithTheParkedLink_OrNoContentWhenThereWasNone()
    {
        var parked = Link(releasePending: true);
        _mediator.SetupSequence(m => m.Send(It.Is<RemoveMeetingLinkCommand>(c => c.SeriesId == Series), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingLinkResponse?>.Success(parked))
            .ReturnsAsync(Result<MeetingLinkResponse?>.Success(null));

        var first = Assert.IsType<OkObjectResult>(await Controller.RemoveLink(Series, CancellationToken.None));
        Assert.IsType<NoContentResult>(await Controller.RemoveLink(Series, CancellationToken.None));

        Assert.Equal(parked, first.Value);
    }

    [Fact]
    public async Task RecheckAttendance_PassesTheOccurrenceThrough()
    {
        RecheckMeetingAttendanceCommand? sent = null;
        var status = new MeetingAttendanceStatusResponse("synced", null, Start.AddDays(1), null);
        _mediator.Setup(m => m.Send(It.IsAny<RecheckMeetingAttendanceCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<MeetingAttendanceStatusResponse>> c, CancellationToken _) => sent = (RecheckMeetingAttendanceCommand)c)
            .ReturnsAsync(Result<MeetingAttendanceStatusResponse>.Success(status));

        var result = Assert.IsType<OkObjectResult>(await Controller.RecheckAttendance(Series, Start, CancellationToken.None));

        Assert.Equal((status, Series, Start), (result.Value, sent!.SeriesId, sent.OriginalStart));
    }
}
