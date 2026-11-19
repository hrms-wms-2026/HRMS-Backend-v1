using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;

public class EscalateExceptionCommandValidator : AbstractValidator<EscalateExceptionCommand>
{
    public EscalateExceptionCommandValidator()
    {
        RuleFor(x => x.ExceptionId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}
