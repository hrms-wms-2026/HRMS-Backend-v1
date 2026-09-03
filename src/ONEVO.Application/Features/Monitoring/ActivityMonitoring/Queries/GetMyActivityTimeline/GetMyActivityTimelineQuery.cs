using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyActivityTimeline;

public sealed record GetMyActivityTimelineQuery(DateOnly? Date) : IRequest<Result<ActivityTimelineDto>>;
