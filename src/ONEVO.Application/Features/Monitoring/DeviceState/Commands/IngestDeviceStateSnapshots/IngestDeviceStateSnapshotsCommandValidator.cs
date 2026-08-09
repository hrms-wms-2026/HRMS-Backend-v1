using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public class IngestDeviceStateSnapshotsCommandValidator : AbstractValidator<IngestDeviceStateSnapshotsCommand>
{
    public const int MaxBatchSize = 200;
    public const int MaxIdleSeconds = 86_400; // 24h ceiling — a stuck/unlocked machine, not a legitimate sample

    public IngestDeviceStateSnapshotsCommandValidator()
    {
        RuleFor(x => x.Snapshots)
            .NotEmpty()
            .WithMessage("At least one snapshot is required.")
            .Must(s => s.Count <= MaxBatchSize)
            .WithMessage($"Batch cannot exceed {MaxBatchSize} snapshots.");

        RuleForEach(x => x.Snapshots).ChildRules(item =>
        {
            item.RuleFor(s => s.IdleSeconds)
                .InclusiveBetween(0, MaxIdleSeconds)
                .WithMessage($"IdleSeconds must be between 0 and {MaxIdleSeconds}.");
        });
    }
}
