using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public record CreatePositionCommand(
    Guid LegalEntityId,
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>;
