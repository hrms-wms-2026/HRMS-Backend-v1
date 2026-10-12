using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

/// <summary>My Team - Team Progress (spec §8.3): progress of the work in modules the caller
/// effectively owns. OverdueLimit 1..50, default 10.</summary>
public sealed record GetLedWorkProgressQuery(int OverdueLimit = 10) : IRequest<Result<LedWorkProgressResponse>>;
