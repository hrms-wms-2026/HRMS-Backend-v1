using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;

public sealed class SyncHolidayCalendarCommandHandler(
    ICurrentUser currentUser,
    IHolidayCalendarSettingsRepository settingsRepo,
    INagerHolidaysClient client,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SyncHolidayCalendarCommand, Result>
{
    public async Task<Result> Handle(SyncHolidayCalendarCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result.Forbidden();
        var tenantId = currentUser.TenantId;

        var settings = await settingsRepo.GetTrackedByIdAsync(tenantId, request.SettingsId, ct);
        if (settings is null) return Result.NotFound("Holiday calendar settings not found.");
        if (!settings.HolidaySyncEnabled) return Result.Failure("Holiday sync is disabled for this legal entity.", 409);

        IReadOnlyList<NagerHoliday> holidays;
        try
        {
            holidays = await client.GetPublicHolidaysAsync(settings.EffectiveCountryCode, request.Year, ct);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure($"Could not reach the holiday provider: {ex.Message}", 502);
        }

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await events.RemoveHolidayEventsForYearAsync(tenantId, request.Year, innerCt);
            foreach (var holiday in holidays)
            {
                var startOfDay = new DateTimeOffset(holiday.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                await events.AddAsync(new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Title = holiday.Name,
                    StartDate = startOfDay,
                    EndDate = startOfDay.AddDays(1),
                    IsAllDay = true,
                    SourceType = CalendarEventSourceTypes.Holiday,
                    ExternalSource = CalendarExternalSources.CountryHoliday,
                    CreatedById = currentUser.UserId
                }, innerCt);
            }

            settings.LastSyncedYear = request.Year;
            settings.LastSyncedAt = DateTimeOffset.UtcNow;
            settingsRepo.Update(settings);

            return await unitOfWork.SaveChangesAsync(innerCt);
        }, ct);

        return Result.Success();
    }
}
