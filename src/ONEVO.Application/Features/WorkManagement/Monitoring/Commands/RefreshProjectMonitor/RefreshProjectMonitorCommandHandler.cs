using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Commands.RefreshProjectMonitor;

public sealed class RefreshProjectMonitorCommandHandler : IRequestHandler<RefreshProjectMonitorCommand, Result>
{
    private readonly IProjectMonitorCallerResolver _callers;
    private readonly IProjectMonitorService _monitor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public RefreshProjectMonitorCommandHandler(
        IProjectMonitorCallerResolver callers, IProjectMonitorService monitor, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _callers = callers;
        _monitor = monitor;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(RefreshProjectMonitorCommand command, CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(command.ProjectId, ct);
        if (!caller.IsSuccess)
            return Result.Failure(caller.Error!, caller.StatusCode ?? 403);

        await _monitor.EvaluateProjectAsync(caller.Value!.TenantId, command.ProjectId, _clock.Today, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
