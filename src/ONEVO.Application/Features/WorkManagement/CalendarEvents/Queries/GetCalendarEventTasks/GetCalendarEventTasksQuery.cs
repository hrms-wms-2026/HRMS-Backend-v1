using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;

public sealed record GetCalendarEventTasksQuery(Guid CalendarEventId) : IRequest<Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>>;
