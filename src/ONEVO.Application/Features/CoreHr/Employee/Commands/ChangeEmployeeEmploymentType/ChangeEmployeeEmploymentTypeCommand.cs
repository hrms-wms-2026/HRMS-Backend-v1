using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeeEmploymentType;

/// <summary>Changes only the employment type - unlike UpdateEmployeeJobDetailsCommand, which
/// overwrites employee number and work mode too and so can't be applied in bulk.</summary>
public sealed record ChangeEmployeeEmploymentTypeCommand(Guid EmployeeId, string EmploymentTypeCode) : IRequest<Result<Unit>>;

public sealed class ChangeEmployeeEmploymentTypeCommandValidator : AbstractValidator<ChangeEmployeeEmploymentTypeCommand>
{
    public ChangeEmployeeEmploymentTypeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.EmploymentTypeCode).NotEmpty().MaximumLength(50);
    }
}
