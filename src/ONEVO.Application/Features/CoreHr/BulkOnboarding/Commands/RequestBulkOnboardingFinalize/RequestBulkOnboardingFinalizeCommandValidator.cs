using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;

/// <summary>Task 19. A batch holds at most <see cref="CsvBatchParser.MaxRows"/> rows, so one finalize request never
/// names more drafts than that.</summary>
public sealed class RequestBulkOnboardingFinalizeCommandValidator : AbstractValidator<RequestBulkOnboardingFinalizeCommand>
{
    public RequestBulkOnboardingFinalizeCommandValidator()
    {
        RuleFor(x => x.OnboardingDraftIds).ValidIdList(CsvBatchParser.MaxRows);
    }
}
