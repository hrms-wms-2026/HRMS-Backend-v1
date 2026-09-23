using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;

public sealed record CancelOffboardingCommand(Guid EmployeeId) : IRequest<Result>;
