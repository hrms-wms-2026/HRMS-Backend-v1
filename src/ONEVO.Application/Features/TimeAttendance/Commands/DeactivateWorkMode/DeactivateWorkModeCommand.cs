using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.DeactivateWorkMode;

public record DeactivateWorkModeCommand(Guid Id) : IRequest<Result>;
