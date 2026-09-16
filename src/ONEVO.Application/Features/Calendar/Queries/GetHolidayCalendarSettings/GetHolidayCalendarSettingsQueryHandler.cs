using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetHolidayCalendarSettings;

public sealed class GetHolidayCalendarSettingsQueryHandler(
    ICurrentUser currentUser,
    IHolidayCalendarSettingsRepository settingsRepo)
    : IRequestHandler<GetHolidayCalendarSettingsQuery, Result<HolidayCalendarSettingsResponse>>
{
    public async Task<Result<HolidayCalendarSettingsResponse>> Handle(GetHolidayCalendarSettingsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result<HolidayCalendarSettingsResponse>.Forbidden();
        if (currentUser.LegalEntityId is not Guid legalEntityId)
            return Result<HolidayCalendarSettingsResponse>.UnprocessableEntity(
                "Select an active company before viewing holiday calendar settings.");

        var settings = await settingsRepo.GetByLegalEntityAsync(currentUser.TenantId, legalEntityId, ct);
        if (settings is null)
            return Result<HolidayCalendarSettingsResponse>.NotFound("Holiday calendar settings have not been set up for this company yet.");

        return Result<HolidayCalendarSettingsResponse>.Success(new HolidayCalendarSettingsResponse(
            settings.Id, settings.LegalEntityId, settings.DefaultCountryCode, settings.OverrideCountryCode,
            settings.HolidaySyncEnabled, settings.LastSyncedYear, settings.LastSyncedAt));
    }
}
