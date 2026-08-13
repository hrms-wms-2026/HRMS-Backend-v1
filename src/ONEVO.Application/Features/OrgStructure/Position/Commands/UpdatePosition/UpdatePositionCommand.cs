using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public record UpdatePositionCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid DepartmentId,
    string Name,
    string Code,
    int MaxOccupancy,
    Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>;
