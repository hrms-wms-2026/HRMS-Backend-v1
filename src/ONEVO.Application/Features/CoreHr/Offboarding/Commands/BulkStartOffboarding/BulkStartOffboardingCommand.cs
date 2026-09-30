using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Services;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.BulkStartOffboarding;

/// <summary>Starts offboarding for many employees with shared exit details. Each employee's
/// checklist is still chosen and completed one at a time in the offboarding wizard.</summary>
public sealed record BulkStartOffboardingCommand(
    IReadOnlyList<Guid> EmployeeIds,
    string Reason,
    DateOnly LastWorkingDate,
    string KnowledgeRiskLevel,
    string? RehireEligibility,
    string? Notes) : IRequest<Result<BulkEmployeeActionResponse>>;

public sealed class BulkStartOffboardingCommandValidator : AbstractValidator<BulkStartOffboardingCommand>
{
    public BulkStartOffboardingCommandValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty()
            .Must(ids => ids.Distinct().Count() <= BulkEmployeeActionRunner.MaxEmployees)
            .WithMessage($"A bulk action can include at most {BulkEmployeeActionRunner.MaxEmployees} employees.");
        RuleFor(x => x.Reason).NotEmpty();
        RuleFor(x => x.KnowledgeRiskLevel).NotEmpty();
    }
}

public sealed class BulkStartOffboardingCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkStartOffboardingCommandHandler> logger)
    : IRequestHandler<BulkStartOffboardingCommand, Result<BulkEmployeeActionResponse>>
{
    public async Task<Result<BulkEmployeeActionResponse>> Handle(BulkStartOffboardingCommand command, CancellationToken ct)
    {
        var response = await BulkEmployeeActionRunner.RunAsync(command.EmployeeIds, async (employeeId, token) =>
        {
            var result = await mediator.Send(new StartOffboardingCommand(
                employeeId, command.Reason, command.LastWorkingDate, command.KnowledgeRiskLevel, command.RehireEligibility, command.Notes), token);
            return new BulkItemOutcome(result.IsSuccess, false, result.Error);
        }, unitOfWork, logger, ct);

        return Result<BulkEmployeeActionResponse>.Success(response);
    }
}
