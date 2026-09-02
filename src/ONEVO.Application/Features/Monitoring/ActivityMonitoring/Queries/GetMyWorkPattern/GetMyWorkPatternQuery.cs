using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyWorkPattern;

/// <summary>Focus / Meeting / Admin minutes per day in [From, To] for the current employee -
/// past days from the daily aggregation job, today computed live from raw snapshots (that job
/// hasn't run for today yet), future days always zero.</summary>
public sealed record GetMyWorkPatternQuery(DateOnly From, DateOnly To) : IRequest<Result<WorkPatternResponse>>;
