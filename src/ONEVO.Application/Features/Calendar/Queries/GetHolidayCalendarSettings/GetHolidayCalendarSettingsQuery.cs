using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetHolidayCalendarSettings;

public sealed record HolidayCalendarSettingsResponse(
    Guid Id, Guid LegalEntityId, string DefaultCountryCode, string? OverrideCountryCode,
    bool HolidaySyncEnabled, int? LastSyncedYear, DateTimeOffset? LastSyncedAt);

public sealed record GetHolidayCalendarSettingsQuery : IRequest<Result<HolidayCalendarSettingsResponse>>;
