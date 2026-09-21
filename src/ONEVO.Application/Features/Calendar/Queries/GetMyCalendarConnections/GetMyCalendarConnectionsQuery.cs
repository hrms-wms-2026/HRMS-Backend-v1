using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;

public sealed record GetMyCalendarConnectionsQuery : IRequest<Result<CalendarConnectionsResponse>>;
