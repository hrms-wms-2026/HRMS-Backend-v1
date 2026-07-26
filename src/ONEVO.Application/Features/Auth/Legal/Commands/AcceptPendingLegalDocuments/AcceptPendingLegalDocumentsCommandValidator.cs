using FluentValidation;

namespace ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;

public sealed class AcceptPendingLegalDocumentsCommandValidator : AbstractValidator<AcceptPendingLegalDocumentsCommand>
{
    public AcceptPendingLegalDocumentsCommandValidator()
    {
        RuleFor(c => c.LegalChallenge).NotEmpty();
        RuleFor(c => c.LegalCsrfToken).NotEmpty();
        RuleFor(c => c.Acceptances).NotEmpty();
    }
}
