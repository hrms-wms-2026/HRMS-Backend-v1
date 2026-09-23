using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;

public class CreateWorkModeCommandHandler : IRequestHandler<CreateWorkModeCommand, Result<WorkModeResponse>>
{
    public const int MaxActiveWorkModesPerLegalEntity = 5;

    private readonly IWorkModeRepository _workModes;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateWorkModeCommandHandler(
        IWorkModeRepository workModes,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _workModes = workModes;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<WorkModeResponse>> Handle(CreateWorkModeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkModeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<WorkModeResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<WorkModeResponse>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<WorkModeResponse>.Conflict("Legal entity is inactive.");

        var activeCount = await _workModes.CountActiveAsync(tenantId, request.LegalEntityId, ct);
        if (activeCount >= MaxActiveWorkModesPerLegalEntity)
            return Result<WorkModeResponse>.Conflict(
                $"This legal entity already has the maximum of {MaxActiveWorkModesPerLegalEntity} work modes. Deactivate one before adding another.");

        var name = request.Name.Trim();
        if (await _workModes.NameExistsAsync(tenantId, request.LegalEntityId, name, excludingId: null, ct))
            return Result<WorkModeResponse>.Conflict("A work mode with this name already exists for this legal entity.");

        var now = _clock.UtcNow;
        var workMode = new WorkMode
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            Name = name,
            BiometricEnabled = request.BiometricEnabled,
            WebEnabled = request.WebEnabled,
            TrayEnabled = request.TrayEnabled,
            PhotoRequired = request.PhotoRequired,
            SelfRegistersLocation = request.SelfRegistersLocation,
            AllowsDailyLocationChoice = request.AllowsDailyLocationChoice,
            IsSystemSeeded = false,
            DisplayOrder = activeCount,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _workModes.AddAsync(workMode, ct);
        await _workModes.SaveChangesAsync(ct);

        return Result<WorkModeResponse>.Success(WorkModeMapper.ToResponse(workMode));
    }
}
