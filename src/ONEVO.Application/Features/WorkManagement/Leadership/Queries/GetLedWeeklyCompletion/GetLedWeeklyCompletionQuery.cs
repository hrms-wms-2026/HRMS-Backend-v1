using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;

/// <summary>Team Progress weekly-completion chart - estimated hours of the led-scope tasks completed
/// in each of the last <see cref="Weeks"/> ISO weeks (GET api/v1/work/led-progress/weekly-completion).
/// Weeks is 1..26 (default 10).</summary>
public sealed record GetLedWeeklyCompletionQuery(
    int Weeks = LedCompletionWeeks.DefaultWeeks,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null) : IRequest<Result<LedWeeklyCompletionResponse>>;
