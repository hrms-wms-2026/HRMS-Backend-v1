using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed record GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To) : IRequest<Result<CalendarEventsResponse>>;
