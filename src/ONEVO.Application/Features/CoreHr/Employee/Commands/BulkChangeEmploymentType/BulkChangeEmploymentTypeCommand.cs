using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeeEmploymentType;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmploymentType;

public sealed record BulkChangeEmploymentTypeCommand(IReadOnlyList<Guid> EmployeeIds, string EmploymentTypeCode)
    : IRequest<Result<BulkEmployeeActionResponse>>;

public sealed class BulkChangeEmploymentTypeCommandValidator : AbstractValidator<BulkChangeEmploymentTypeCommand>
{
    public BulkChangeEmploymentTypeCommandValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty()
            .Must(ids => ids.Distinct().Count() <= BulkEmployeeActionRunner.MaxEmployees)
            .WithMessage($"A bulk action can include at most {BulkEmployeeActionRunner.MaxEmployees} employees.");
        RuleFor(x => x.EmploymentTypeCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class BulkChangeEmploymentTypeCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkChangeEmploymentTypeCommandHandler> logger)
    : IRequestHandler<BulkChangeEmploymentTypeCommand, Result<BulkEmployeeActionResponse>>
{
    public async Task<Result<BulkEmployeeActionResponse>> Handle(BulkChangeEmploymentTypeCommand command, CancellationToken ct)
    {
        var response = await BulkEmployeeActionRunner.RunAsync(command.EmployeeIds, async (employeeId, token) =>
        {
            var result = await mediator.Send(new ChangeEmployeeEmploymentTypeCommand(employeeId, command.EmploymentTypeCode), token);
            return new BulkItemOutcome(result.IsSuccess, false, result.Error);
        }, unitOfWork, logger, ct);

        return Result<BulkEmployeeActionResponse>.Success(response);
    }
}
