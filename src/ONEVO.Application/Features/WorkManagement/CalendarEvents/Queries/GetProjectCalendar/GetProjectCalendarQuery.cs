using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;

public sealed record GetProjectCalendarQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ProjectCalendarItemResponse>>>;
