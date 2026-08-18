using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public class CreateTaskEditRequestCommandValidator : AbstractValidator<CreateTaskEditRequestCommand>
{
    public CreateTaskEditRequestCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Priority).Must(p => p is WorkTaskPriorities.Low or WorkTaskPriorities.Medium or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
        RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue);
    }
}
