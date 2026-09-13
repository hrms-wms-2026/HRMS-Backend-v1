using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;

public class UpdateWorkModeCommandHandler : IRequestHandler<UpdateWorkModeCommand, Result<WorkModeResponse>>
{
    private readonly IWorkModeRepository _workModes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateWorkModeCommandHandler(
        IWorkModeRepository workModes, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _workModes = workModes;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<WorkModeResponse>> Handle(UpdateWorkModeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkModeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<WorkModeResponse>.Forbidden("Tenant context missing.");

        var workMode = await _workModes.GetTrackedByIdAsync(tenantId, request.Id, ct);
        if (workMode is null)
            return Result<WorkModeResponse>.NotFound("Work mode not found.");

        var name = request.Name.Trim();
        if (await _workModes.NameExistsAsync(tenantId, workMode.LegalEntityId, name, excludingId: workMode.Id, ct))
            return Result<WorkModeResponse>.Conflict("A work mode with this name already exists for this legal entity.");

        workMode.Name = name;
        workMode.BiometricEnabled = request.BiometricEnabled;
        workMode.WebEnabled = request.WebEnabled;
        workMode.TrayEnabled = request.TrayEnabled;
        workMode.PhotoRequired = request.PhotoRequired;
        workMode.UpdatedAt = _clock.UtcNow;

        _workModes.Update(workMode);
        await _workModes.SaveChangesAsync(ct);

        return Result<WorkModeResponse>.Success(WorkModeMapper.ToResponse(workMode));
    }
}
