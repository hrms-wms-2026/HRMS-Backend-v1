using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;

/// <summary>Team Progress weekly-completion chart - estimated hours of the led-scope tasks completed
/// in each of the last <see cref="Weeks"/> ISO weeks (GET api/v1/work/led-progress/weekly-completion).
/// Weeks is 1..26 (default 10). Period (optional) is week | month: the buckets become the days of
/// the current ISO week, or the ISO weeks of the current month clipped to it, and Weeks is ignored.</summary>
public sealed record GetLedWeeklyCompletionQuery(
    int Weeks = LedCompletionWeeks.DefaultWeeks,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null,
    string? Period = null) : IRequest<Result<LedWeeklyCompletionResponse>>;
