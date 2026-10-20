using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using LeaveTypeEntity = ONEVO.Domain.Features.Leave.Type.Entities.LeaveType;

namespace ONEVO.Application.Features.Leave.Type.Mappers;

public static class LeaveTypeMapper
{
    public static LeaveTypeResponse ToResponse(LeaveTypeEntity entity, int policyCount = 0) => new(
        entity.Id,
        entity.Name,
        entity.Code,
        entity.Description,
        entity.Color,
        entity.IsPaid,
        entity.ApplicableGender,
        entity.RequiresDocument,
        entity.DocumentRequiredAfterHours,
        entity.AcceptedDocumentTypes,
        entity.IsActive,
        entity.CreatedAt,
        policyCount);
}
