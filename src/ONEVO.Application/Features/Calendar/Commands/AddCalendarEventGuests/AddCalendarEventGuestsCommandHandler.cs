using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Helpers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.AddCalendarEventGuests;

public sealed record AddCalendarEventGuestsCommand(Guid EventId, IReadOnlyList<string> Emails)
    : IRequest<Result<CalendarEventGuestsResult>>;

public sealed record CalendarEventGuestsResult(IReadOnlyList<CalendarEventGuestSummary> Guests);

/// <summary>Invites external guests (email only, no employee record) to an existing event.
/// Organizer-only, like every other event-mutation handler. Each new guest gets the invite email
/// with the event's meeting link when it already has one.</summary>
public sealed class AddCalendarEventGuestsCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AddCalendarEventGuestsCommand, Result<CalendarEventGuestsResult>>
{
    public async Task<Result<CalendarEventGuestsResult>> Handle(AddCalendarEventGuestsCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventGuestsResult>.Forbidden();

        var (normalized, invalidEmail) = CalendarEventValidation.NormalizeGuestEmails(request.Emails);
        if (invalidEmail is not null)
            return Result<CalendarEventGuestsResult>.Failure($"'{invalidEmail}' is not a valid email address.", 400);

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result<CalendarEventGuestsResult>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CalendarEventGuestsResult>.Forbidden("Only the event organizer can invite guests.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var guestsByEvent = await events.GetGuestsForEventsAsync(tenantId, [existing.Id], innerCt);
            var currentGuests = guestsByEvent.TryGetValue(existing.Id, out var g) ? g.ToList() : new List<CalendarEventGuest>();
            var existingEmails = currentGuests.Select(x => x.Email).ToHashSet();

            var newEmails = normalized.Where(email => !existingEmails.Contains(email)).ToList();
            if (currentGuests.Count + newEmails.Count > CalendarEventValidation.MaxGuestsPerEvent)
                return Result<CalendarEventGuestsResult>.Failure(
                    $"An event can have at most {CalendarEventValidation.MaxGuestsPerEvent} guests.", 400);

            if (newEmails.Count > 0)
            {
                var newGuests = newEmails.Select(email => new CalendarEventGuest
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = existing.Id, Email = email
                }).ToList();
                await events.AddGuestsAsync(newGuests, innerCt);
                currentGuests.AddRange(newGuests);

                var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
                await notifications.NotifyGuestsAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    newEmails, organizerName, existing.MeetingLink, innerCt);

                await unitOfWork.SaveChangesAsync(innerCt);
            }

            return Result<CalendarEventGuestsResult>.Success(
                new CalendarEventGuestsResult(currentGuests.Select(x => new CalendarEventGuestSummary(x.Email)).ToList()));
        }, ct);
    }
}
