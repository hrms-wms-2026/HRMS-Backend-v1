using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;

public record UpdateWorkModeCommand(
    Guid Id,
    string Name,
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired) : IRequest<Result<WorkModeResponse>>;
