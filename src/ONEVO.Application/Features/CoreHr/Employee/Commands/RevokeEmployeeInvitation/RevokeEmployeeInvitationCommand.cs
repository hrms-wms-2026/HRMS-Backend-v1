using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

public sealed record RevokeEmployeeInvitationCommand(Guid EmployeeId) : IRequest<Result<Unit>>;
