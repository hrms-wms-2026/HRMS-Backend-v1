using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;

/// <summary>Task 19. legal_acceptance_records: document_type nvarchar(80), document_version nvarchar(50), decision
/// nvarchar(20), ip_address nvarchar(45), user_agent nvarchar(500); legal_document_versions.content_hash is
/// nvarchar(128). There are only a handful of required documents, so 20 items per request is ample. An empty list and
/// unknown decisions are still answered by the handler.</summary>
public sealed class SubmitLegalAcceptanceCommandValidator : AbstractValidator<SubmitLegalAcceptanceCommand>
{
    public const int MaxItems = 20;
    public const int DocumentTypeMaxLength = 80;
    public const int VersionMaxLength = 50;
    public const int DecisionMaxLength = 20;
    public const int ContentHashMaxLength = 128;

    public SubmitLegalAcceptanceCommandValidator()
    {
        RuleFor(x => x.Acceptances)
            .Must(items => items is null || items.Count <= MaxItems)
            .WithMessage($"'{{PropertyName}}' can hold at most {MaxItems} entries.");
        RuleForEach(x => x.Acceptances).ChildRules(item =>
        {
            item.RuleFor(i => i.DocumentType).ValidName(DocumentTypeMaxLength);
            item.RuleFor(i => i.Version).ValidName(VersionMaxLength);
            item.RuleFor(i => i.Decision).ValidName(DecisionMaxLength);
            item.RuleFor(i => i.ContentHash).ValidName(ContentHashMaxLength, required: false);
        });
        RuleFor(x => x.IpAddress).ValidName(AuthInputRules.IpAddressMaxLength, required: false);
        RuleFor(x => x.UserAgent).ValidName(AuthInputRules.UserAgentMaxLength, required: false);
    }
}
