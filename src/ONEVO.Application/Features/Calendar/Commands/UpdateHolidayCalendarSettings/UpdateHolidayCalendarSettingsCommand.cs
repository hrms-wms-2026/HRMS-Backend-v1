using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateHolidayCalendarSettings;

public sealed record UpdateHolidayCalendarSettingsCommand(
    Guid SettingsId, string? OverrideCountryCode, bool HolidaySyncEnabled) : IRequest<Result>;
