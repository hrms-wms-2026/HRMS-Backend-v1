using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmployeePosition;

public sealed record BulkChangeEmployeePositionCommand(
    IReadOnlyList<Guid> EmployeeIds,
    Guid PositionId,
    DateOnly EffectiveFrom,
    string ChangeReason,
    Guid? ReportsToEmployeeId) : IRequest<Result<BulkEmployeeActionResponse>>;

public sealed class BulkChangeEmployeePositionCommandValidator : AbstractValidator<BulkChangeEmployeePositionCommand>
{
    public BulkChangeEmployeePositionCommandValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty()
            .Must(ids => ids.Distinct().Count() <= BulkEmployeeActionRunner.MaxEmployees)
            .WithMessage($"A bulk action can include at most {BulkEmployeeActionRunner.MaxEmployees} employees.");
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.ChangeReason).NotEmpty();
    }
}

public sealed class BulkChangeEmployeePositionCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkChangeEmployeePositionCommandHandler> logger)
    : IRequestHandler<BulkChangeEmployeePositionCommand, Result<BulkEmployeeActionResponse>>
{
    public async Task<Result<BulkEmployeeActionResponse>> Handle(BulkChangeEmployeePositionCommand command, CancellationToken ct)
    {
        var response = await BulkEmployeeActionRunner.RunAsync(command.EmployeeIds, async (employeeId, token) =>
        {
            var result = await mediator.Send(new ChangeEmployeePositionCommand(
                employeeId, command.PositionId, command.EffectiveFrom, command.ChangeReason, command.ReportsToEmployeeId), token);
            return new BulkItemOutcome(result.IsSuccess, result.IsSuccess && result.Value!.PendingApproval, result.Error);
        }, unitOfWork, logger, ct);

        return Result<BulkEmployeeActionResponse>.Success(response);
    }
}
