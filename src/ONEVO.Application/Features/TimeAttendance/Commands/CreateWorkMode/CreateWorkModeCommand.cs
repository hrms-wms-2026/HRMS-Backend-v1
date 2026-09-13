using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;

public record CreateWorkModeCommand(
    Guid LegalEntityId,
    string Name,
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired) : IRequest<Result<WorkModeResponse>>;
