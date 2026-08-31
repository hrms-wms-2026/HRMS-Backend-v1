using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;

public sealed class CreateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCalendarEventCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(CreateCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CalendarEventItem>.Failure("Title is required.", 400);

        if (request.Recurrence != CalendarRecurrences.None && string.IsNullOrWhiteSpace(request.RecurrenceRule))
            return Result<CalendarEventItem>.Failure("RecurrenceRule is required when Recurrence is not 'none'.", 400);

        var tenantId = currentUser.TenantId;

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Title = request.Title.Trim(),
                Description = request.Description, StartDate = request.StartDate, EndDate = request.EndDate,
                SourceType = CalendarEventSourceTypes.Manual, IsAllDay = request.IsAllDay,
                Timezone = request.Timezone, Location = request.Location, MeetingLink = request.MeetingLink,
                Color = request.Color, Recurrence = request.Recurrence, RecurrenceRule = request.RecurrenceRule
            };
            await events.AddAsync(calendarEvent, innerCt);

            if (request.ParticipantEmployeeIds.Count > 0)
            {
                var participants = request.ParticipantEmployeeIds.Select(employeeId => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = calendarEvent.Id, EmployeeId = employeeId,
                    ResponseStatus = CalendarEventParticipantStatuses.Pending
                }).ToList();
                await events.AddParticipantsAsync(participants, innerCt);
            }

            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                calendarEvent.Id, calendarEvent.Title, calendarEvent.Description, calendarEvent.StartDate,
                calendarEvent.EndDate, calendarEvent.SourceType, calendarEvent.Color, calendarEvent.Recurrence,
                calendarEvent.IsAllDay, calendarEvent.Timezone, calendarEvent.EventStatus, calendarEvent.IsPrivate,
                calendarEvent.Location, calendarEvent.MeetingLink, calendarEvent.ExternalSource, calendarEvent.CreatedById));
        }, ct);
    }
}
