using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.DeactivateWorkMode;

// No in-use guard: deactivating a Work Mode currently assigned to employees is allowed.
// Employee.WorkModeId, AttendanceRecord.ExpectedWorkModeId, and WorkAreaChangeRequest's
// WorkModeId columns all keep resolving by FK - see spec Component 1. Admins reassign
// affected employees manually; there is no bulk-reassignment wizard in this design.
public class DeactivateWorkModeCommandHandler : IRequestHandler<DeactivateWorkModeCommand, Result>
{
    private readonly IWorkModeRepository _workModes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public DeactivateWorkModeCommandHandler(
        IWorkModeRepository workModes, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _workModes = workModes;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(DeactivateWorkModeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var workMode = await _workModes.GetTrackedByIdAsync(_currentUser.TenantId, request.Id, ct);
        if (workMode is null)
            return Result.NotFound("Work mode not found.");

        workMode.IsActive = false;
        workMode.UpdatedAt = _clock.UtcNow;
        await _workModes.SaveChangesAsync(ct);

        return Result.Success();
    }
}
