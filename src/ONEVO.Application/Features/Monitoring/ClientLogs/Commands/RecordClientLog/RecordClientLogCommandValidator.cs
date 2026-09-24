using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed class RecordClientLogCommandValidator : AbstractValidator<RecordClientLogCommand>
{
    public RecordClientLogCommandValidator()
    {
        RuleFor(x => x.Level).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}
