using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using LeaveTypeEntity = ONEVO.Domain.Features.Leave.Type.Entities.LeaveType;

namespace ONEVO.Application.Features.Leave.Type.Mappers;

public static class LeaveTypeMapper
{
    public static LeaveTypeResponse ToResponse(LeaveTypeEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.Code,
        entity.Description,
        entity.Category,
        entity.IsPaid,
        entity.RequiresApproval,
        entity.RequiresDocument,
        entity.DocumentRequiredAfterDays,
        entity.AcceptedDocumentTypes,
        entity.MaxConsecutiveDays,
        entity.DefaultDaysPerYear,
        entity.CarryForwardAllowed,
        entity.MaxCarryForwardDays,
        entity.CarryForwardExpiryMonths,
        entity.ProRataForNewJoiners,
        entity.ApplicableGender,
        entity.MinimumNoticeDays,
        entity.IsActive,
        entity.CreatedAt);
}
