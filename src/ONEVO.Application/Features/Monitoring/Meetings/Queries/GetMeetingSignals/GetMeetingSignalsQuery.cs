using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;

public record GetMeetingSignalsQuery : IRequest<Result<PagedResult<MeetingSignalDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}
