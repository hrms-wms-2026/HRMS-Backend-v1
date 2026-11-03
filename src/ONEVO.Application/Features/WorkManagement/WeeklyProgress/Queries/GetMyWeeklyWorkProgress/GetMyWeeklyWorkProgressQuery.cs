using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;

namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.Queries.GetMyWeeklyWorkProgress;

/// <summary>GET /work/my-weekly-progress?weekStart=: the caller's week (a Monday; null = the current week in their zone)
/// and the week before it.</summary>
public sealed record GetMyWeeklyWorkProgressQuery(DateOnly? WeekStart) : IRequest<Result<WeeklyWorkProgressResponse>>;
