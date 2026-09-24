using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public sealed record StartOffboardingCommand(
    Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel,
    string? RehireEligibility, string? Notes) : IRequest<Result<Guid>>;
