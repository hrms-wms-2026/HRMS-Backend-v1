using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.ListProjectMonitorAlerts;

/// <summary>The project's open monitor alerts that the caller can see: those on Modules (and their
/// tasks) at or below the caller's Modules, plus sprint alerts. The project lead and the root Module
/// owner see every alert.</summary>
public sealed record ListProjectMonitorAlertsQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<MonitorAlertResponse>>>;
