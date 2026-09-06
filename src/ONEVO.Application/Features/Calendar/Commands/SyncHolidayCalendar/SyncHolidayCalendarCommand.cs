using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;

public sealed record SyncHolidayCalendarCommand(Guid SettingsId, int Year) : IRequest<Result>;
