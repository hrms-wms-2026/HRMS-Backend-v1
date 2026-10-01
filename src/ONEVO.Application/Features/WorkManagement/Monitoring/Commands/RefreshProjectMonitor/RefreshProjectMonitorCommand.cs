using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Commands.RefreshProjectMonitor;

/// <summary>Re-runs the project monitor for one project right now (the Tree calls it when it loads,
/// including right after a save), so red marks do not wait for the hourly ProjectMonitorJob. Same
/// rules and same notify-once behaviour as the job.</summary>
public sealed record RefreshProjectMonitorCommand(Guid ProjectId) : IRequest<Result>;
