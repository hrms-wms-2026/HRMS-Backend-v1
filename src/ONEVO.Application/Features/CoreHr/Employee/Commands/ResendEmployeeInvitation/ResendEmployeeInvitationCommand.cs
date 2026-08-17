using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;

public sealed record ResendEmployeeInvitationCommand(Guid EmployeeId) : IRequest<Result<ResendEmployeeInvitationResponse>>;

public sealed record ResendEmployeeInvitationResponse(DateTimeOffset ExpiresAt);
