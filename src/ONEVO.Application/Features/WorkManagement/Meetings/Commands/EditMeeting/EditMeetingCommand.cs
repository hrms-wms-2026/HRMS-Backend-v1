using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands.EditMeeting;

/// <summary>PATCH /meetings/series/{seriesId}/occurrences/{originalStart} (spec 5.3).</summary>
public sealed record EditMeetingCommand(Guid SeriesId, DateTimeOffset OriginalStart, EditMeetingRequest Edit)
    : IRequest<Result<MeetingEditResponse>>;

public sealed class EditMeetingCommandHandler : IRequestHandler<EditMeetingCommand, Result<MeetingEditResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingEditService _edits;

    public EditMeetingCommandHandler(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingEditService edits)
    {
        _currentUser = currentUser;
        _identity = identity;
        _edits = edits;
    }

    public async Task<Result<MeetingEditResponse>> Handle(EditMeetingCommand command, CancellationToken ct)
    {
        var caller = _currentUser.IsAuthenticated
            ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct)
            : null;
        return caller is Guid employeeId
            ? await _edits.EditAsync(_currentUser.TenantId, employeeId, _currentUser.UserId, command.SeriesId, command.OriginalStart, command.Edit, ct)
            : MeetingReadService.NotFound<MeetingEditResponse>();
    }
}
