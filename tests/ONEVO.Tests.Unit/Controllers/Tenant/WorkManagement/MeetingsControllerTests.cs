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
}
