using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.ConfirmEventMeetingCleanup;

public sealed class ConfirmEventMeetingCleanupCommandHandler(
    ICurrentUser currentUser, ICalendarEventRepository events, ICalendarEventMeetingRepository meetings,
    IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<ConfirmEventMeetingCleanupCommand, Result>
{
    public async Task<Result> Handle(ConfirmEventMeetingCleanupCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can confirm this.");

        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, evt.Id, ct);
        if (meeting is null || meeting.Status != CalendarEventMeetingStatuses.Unresolved)
            return Result.Conflict("meeting_link_not_unresolved");

        meeting.Status = CalendarEventMeetingStatuses.Cancelled;
        meeting.UpdatedAt = clock.UtcNow;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
