using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands.CancelMeeting;

/// <summary>POST /meetings/series/{seriesId}/occurrences/{originalStart}/cancel (spec 5.3).</summary>
public sealed record CancelMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, string Scope) : IRequest<Result<bool>>;

public sealed class CancelMeetingCommandHandler : IRequestHandler<CancelMeetingCommand, Result<bool>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingCancelService _cancels;

    public CancelMeetingCommandHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingCancelService cancels)
    {
        _currentUser = currentUser;
        _identity = identity;
        _cancels = cancels;
    }

    public async Task<Result<bool>> Handle(CancelMeetingCommand command, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await _cancels.CancelAsync(_currentUser.TenantId, employeeId, _currentUser.UserId, command.SeriesId, command.OriginalStart, command.Scope, ct)
            : MeetingReadService.NotFound<bool>();
    }
}
