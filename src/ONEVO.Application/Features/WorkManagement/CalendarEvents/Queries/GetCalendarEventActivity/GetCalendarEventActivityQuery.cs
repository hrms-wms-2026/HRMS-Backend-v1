using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;

public sealed record GetCalendarEventActivityQuery(Guid CalendarEventId) : IRequest<Result<IReadOnlyList<CalendarEventActivityEntryResponse>>>;
