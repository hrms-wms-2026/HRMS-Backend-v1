using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

/// <param name="ForEmployeeId">Someone else's calendar, for their active meeting delegate (spec "Seeing the principal's
/// calendar"); null or the caller means the caller's own.</param>
public sealed record GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To, Guid? ForEmployeeId = null)
    : IRequest<Result<CalendarEventsResponse>>;
