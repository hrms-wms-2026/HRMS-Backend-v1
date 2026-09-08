using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;

namespace ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;

public sealed class PreviewWorkAreaChangeRequestCommandValidator
    : AbstractValidator<PreviewWorkAreaChangeRequestCommand>
{
    public PreviewWorkAreaChangeRequestCommandValidator()
    {
        RuleFor(x => x.Date).Must(x => x != default).WithMessage("A date is required.");
        RuleFor(x => x.RequestedWorkArea)
            .NotEmpty()
            .Must(IsSupportedWorkArea)
            .WithMessage("Requested work area must be onsite or remote.");
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A reason is required.");
    }

    internal static bool IsSupportedWorkArea(string? value)
        => value?.Trim().ToLowerInvariant() is "onsite" or "remote";
}

public sealed class CreateWorkAreaChangeRequestCommandValidator
    : AbstractValidator<CreateWorkAreaChangeRequestCommand>
{
    public CreateWorkAreaChangeRequestCommandValidator()
    {
        RuleFor(x => x.Date).Must(x => x != default).WithMessage("A date is required.");
        RuleFor(x => x.RequestedWorkArea)
            .NotEmpty()
            .Must(PreviewWorkAreaChangeRequestCommandValidator.IsSupportedWorkArea)
            .WithMessage("Requested work area must be onsite or remote.");
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A reason is required.");
    }
}

public sealed class ApproveWorkAreaChangeRequestCommandValidator
    : AbstractValidator<ApproveWorkAreaChangeRequestCommand>
{
    public ApproveWorkAreaChangeRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReviewComment).MaximumLength(2000);
    }
}

public sealed class RejectWorkAreaChangeRequestCommandValidator
    : AbstractValidator<RejectWorkAreaChangeRequestCommand>
{
    public RejectWorkAreaChangeRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReviewComment).NotEmpty().MaximumLength(2000);
    }
}

public sealed class CancelWorkAreaChangeRequestCommandValidator
    : AbstractValidator<CancelWorkAreaChangeRequestCommand>
{
    public CancelWorkAreaChangeRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
