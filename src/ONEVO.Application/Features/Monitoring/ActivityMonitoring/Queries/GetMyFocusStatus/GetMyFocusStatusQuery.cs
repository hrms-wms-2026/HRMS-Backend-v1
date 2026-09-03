using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyFocusStatus;

/// <summary>Whether the current employee is right now in an ongoing focus streak long enough
/// to warrant a mindful-break nudge, for the Wellbeing dashboard widget (and, via the same
/// endpoint, the desktop tray app's own notification later).</summary>
public sealed record GetMyFocusStatusQuery : IRequest<Result<FocusStatusResponse>>;
