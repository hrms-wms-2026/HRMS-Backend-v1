using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

public sealed record CompleteOffboardingCommand(Guid EmployeeId) : IRequest<Result>;
