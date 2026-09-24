using FluentValidation;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed class ClockInCommandValidator : AbstractValidator<ClockInCommand>
{
    public ClockInCommandValidator()
    {
        RuleFor(x => x.Source)
            .Must(source => string.Equals(source?.Trim(), AttendanceRecord.SourceWeb, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Source must be web.");
    }
}
