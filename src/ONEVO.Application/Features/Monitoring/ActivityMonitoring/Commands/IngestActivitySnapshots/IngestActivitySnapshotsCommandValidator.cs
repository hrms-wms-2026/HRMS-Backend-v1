using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

public class IngestActivitySnapshotsCommandValidator : AbstractValidator<IngestActivitySnapshotsCommand>
{
    public const int MaxBatchSize = 200;
    public const int MaxEventsCount = 100_000;
    public const int MaxIntervalSeconds = 300;
    public const int MaxProcessNameLength = 100;

    public IngestActivitySnapshotsCommandValidator()
    {
        RuleFor(x => x.Snapshots)
            .NotEmpty()
            .WithMessage("At least one snapshot is required.")
            .Must(s => s.Count <= MaxBatchSize)
            .WithMessage($"Batch cannot exceed {MaxBatchSize} snapshots.");

        RuleForEach(x => x.Snapshots).ChildRules(item =>
        {
            item.RuleFor(s => s.KeyboardEventsCount)
                .InclusiveBetween(0, MaxEventsCount)
                .WithMessage($"KeyboardEventsCount must be between 0 and {MaxEventsCount}.");

            item.RuleFor(s => s.MouseEventsCount)
                .InclusiveBetween(0, MaxEventsCount)
                .WithMessage($"MouseEventsCount must be between 0 and {MaxEventsCount}.");

            item.RuleFor(s => s)
                .Must(s => s.ActiveSeconds + s.IdleSeconds <= MaxIntervalSeconds)
                .WithMessage($"ActiveSeconds + IdleSeconds cannot exceed {MaxIntervalSeconds}.");

            item.RuleFor(s => s.ActiveSeconds)
                .GreaterThanOrEqualTo(0);

            item.RuleFor(s => s.IdleSeconds)
                .GreaterThanOrEqualTo(0);

            item.RuleFor(s => s.IntensityScore)
                .InclusiveBetween(0, 100)
                .WithMessage("IntensityScore must be between 0 and 100.");

            item.RuleFor(s => s.ForegroundProcessName)
                .MaximumLength(MaxProcessNameLength)
                .When(s => s.ForegroundProcessName is not null)
                .WithMessage($"ForegroundProcessName must not exceed {MaxProcessNameLength} characters.");

            item.RuleFor(s => s.ForegroundProcessName)
                .Must(name => name is null || (!name.Contains('\\') && !name.Contains('/') && !name.Contains(':')))
                .WithMessage("ForegroundProcessName must not contain path separators.");
        });
    }
}
