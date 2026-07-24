using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;

public record GetMeetingsQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<MeetingSessionDto>>>;
