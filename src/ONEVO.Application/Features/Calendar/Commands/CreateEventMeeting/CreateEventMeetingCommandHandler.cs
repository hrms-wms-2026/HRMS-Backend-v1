using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed class CreateEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    ICalendarEventMeetingRepository meetings,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateEventMeetingCommand, Result<CreateEventMeetingResult>>
{
    public async Task<Result<CreateEventMeetingResult>> Handle(CreateEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CreateEventMeetingResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CreateEventMeetingResult>.Forbidden("Only the event organizer can add a meeting.");

        var connection = await connections.GetByTenantUserProviderAsync(tenantId, currentUser.UserId, "outlook_calendar", ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson))
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (accessToken is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        var meetingDto = await teamsClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.MeetingLink = meetingDto.JoinUrl;
            events.Update(existing);

            await meetings.AddAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                ExternalMeetingId = meetingDto.ExternalMeetingId, JoinUrl = meetingDto.JoinUrl,
                OrganizerJoinUrl = meetingDto.OrganizerJoinUrl, PasscodeOrPin = meetingDto.PasscodeOrPin,
                Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            }, innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(meetingDto.JoinUrl));
        }, ct);
    }

    private static bool HasMeetingScope(string scopesJson)
    {
        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
            return scopes.Contains("OnlineMeetings.ReadWrite", StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
