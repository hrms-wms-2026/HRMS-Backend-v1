using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public class IngestAppUsageSnapshotsCommandValidator : AbstractValidator<IngestAppUsageSnapshotsCommand>
{
    public const int MaxBatchSize = 200;
    public const int MaxProcessNameLength = 100;
    public const int MaxHashLength = 128;

    public IngestAppUsageSnapshotsCommandValidator()
    {
        RuleFor(x => x.Snapshots)
            .NotEmpty()
            .WithMessage("At least one snapshot is required.")
            .Must(s => s.Count <= MaxBatchSize)
            .WithMessage($"Batch cannot exceed {MaxBatchSize} snapshots.");

        RuleForEach(x => x.Snapshots).ChildRules(item =>
        {
            item.RuleFor(s => s.ProcessName)
                .MaximumLength(MaxProcessNameLength)
                .When(s => s.ProcessName is not null)
                .WithMessage($"ProcessName must not exceed {MaxProcessNameLength} characters.");

            item.RuleFor(s => s.ProcessName)
                .Must(name => name is null || (!name.Contains('\\') && !name.Contains('/') && !name.Contains(':')))
                .WithMessage("ProcessName must not contain path separators.");

            item.RuleFor(s => s.WindowTitleHash)
                .MaximumLength(MaxHashLength)
                .When(s => s.WindowTitleHash is not null)
                .WithMessage($"WindowTitleHash must not exceed {MaxHashLength} characters.");
        });
    }
}
