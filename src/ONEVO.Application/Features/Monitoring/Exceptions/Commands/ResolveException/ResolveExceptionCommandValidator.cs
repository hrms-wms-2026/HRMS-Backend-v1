using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;

public class ResolveExceptionCommandValidator : AbstractValidator<ResolveExceptionCommand>
{
    public ResolveExceptionCommandValidator()
    {
        RuleFor(x => x.ExceptionId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}
