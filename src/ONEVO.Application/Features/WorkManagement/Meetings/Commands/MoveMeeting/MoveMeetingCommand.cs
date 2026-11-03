using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands.MoveMeeting;

/// <summary>POST /meetings/series/{seriesId}/occurrences/{originalStart}/move (spec 5.5, D9, D20).</summary>
public sealed record MoveMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, MoveMeetingRequest Move)
    : IRequest<Result<MeetingMoveResponse>>;

public sealed class MoveMeetingCommandHandler : IRequestHandler<MoveMeetingCommand, Result<MeetingMoveResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingMoveService _moves;

    public MoveMeetingCommandHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingMoveService moves)
    {
        _currentUser = currentUser;
        _identity = identity;
        _moves = moves;
    }

    public async Task<Result<MeetingMoveResponse>> Handle(MoveMeetingCommand command, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await _moves.MoveAsync(_currentUser.TenantId, employeeId, _currentUser.UserId, command.SeriesId, command.OriginalStart, command.Move, ct)
            : MeetingReadService.NotFound<MeetingMoveResponse>();
    }
}
