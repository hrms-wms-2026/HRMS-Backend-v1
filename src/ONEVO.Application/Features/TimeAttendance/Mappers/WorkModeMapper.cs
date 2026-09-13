using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Mappers;

public static class WorkModeMapper
{
    public static WorkModeResponse ToResponse(WorkMode entity) => new(
        entity.Id,
        entity.LegalEntityId,
        entity.Name,
        entity.BiometricEnabled,
        entity.WebEnabled,
        entity.TrayEnabled,
        entity.PhotoRequired,
        entity.IsSystemSeeded,
        entity.DisplayOrder,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt);
}
