using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;

public sealed record GetCalendarEventByIdQuery(Guid Id) : IRequest<Result<CalendarEventDetailResponse>>;
