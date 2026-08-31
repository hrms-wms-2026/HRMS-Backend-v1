using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;

public sealed class CancelRecurringOccurrenceCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CancelRecurringOccurrenceCommand, Result>
{
    public async Task<Result> Handle(CancelRecurringOccurrenceCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var master = await events.GetTrackedByIdForTenantAsync(tenantId, request.MasterId, ct);
        if (master is null)
            return Result.NotFound("Calendar event not found.");

        if (master.RecurrenceParentId != null || master.Recurrence == CalendarRecurrences.None)
            return Result.Failure("This event is not a recurring series.", 400);

        if (master.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the series creator can cancel an occurrence.");

        var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, ct);
        if (child is null)
        {
            child = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                RecurrenceOriginalStart = request.OriginalStart, IsRecurrenceCancelled = true,
                Title = master.Title, StartDate = request.OriginalStart,
                EndDate = request.OriginalStart + (master.EndDate - master.StartDate),
                SourceType = master.SourceType, Recurrence = CalendarRecurrences.None
            };
            await events.AddAsync(child, ct);
        }
        else
        {
            child.IsRecurrenceCancelled = true;
            events.Update(child);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
