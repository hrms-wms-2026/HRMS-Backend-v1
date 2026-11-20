using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Validation;

/// <summary>
/// Task 19b. legal_document_versions: document_type nvarchar(80), version nvarchar(50), title nvarchar(200),
/// block_scope nvarchar(40). content_json, content_html, content_text and publish_reason are nvarchar(max), so
/// the caps below are chosen: a long terms-of-service document is about 60,000 characters of text, its HTML about
/// twice that and the editor JSON about four times that, so the caps leave several times that room.
/// The handlers keep the allowed document types and block scopes, the required checks, the editor JSON check and
/// the allow-list HTML check (LegalHtmlValidator). ValidRichText adds the shared active-content check.
/// </summary>
public static class LegalDocumentLimits
{
    public const int DocumentTypeMaxLength = 80;
    public const int VersionMaxLength = 50;
    public const int TitleMaxLength = 200;
    public const int BlockScopeMaxLength = 40;
    public const int ContentTextMaxLength = 250_000;
    public const int ContentHtmlMaxLength = 500_000;
    public const int ContentJsonMaxLength = 1_000_000;
    public const int PublishReasonMaxLength = 2_000;
}

/// <summary>Create and update share the content fields.</summary>
public interface ILegalDocumentContentInput
{
    string Title { get; }
    string ContentJson { get; }
    string ContentHtml { get; }
    string ContentText { get; }
    string BlockScope { get; }
}

public abstract class LegalDocumentContentInputValidator<T> : AbstractValidator<T> where T : ILegalDocumentContentInput
{
    protected LegalDocumentContentInputValidator()
    {
        RuleFor(x => x.Title).ValidName(LegalDocumentLimits.TitleMaxLength, required: false);
        RuleFor(x => x.ContentJson).ValidName(LegalDocumentLimits.ContentJsonMaxLength, required: false);
        RuleFor(x => x.ContentHtml).ValidRichText(LegalDocumentLimits.ContentHtmlMaxLength);
        RuleFor(x => x.ContentText).ValidName(LegalDocumentLimits.ContentTextMaxLength, required: false);
        RuleFor(x => x.BlockScope).ValidName(LegalDocumentLimits.BlockScopeMaxLength, required: false);
    }
}

public sealed class CreateLegalDocumentVersionCommandValidator : LegalDocumentContentInputValidator<CreateLegalDocumentVersionCommand>
{
    public CreateLegalDocumentVersionCommandValidator()
    {
        RuleFor(x => x.DocumentType).ValidName(LegalDocumentLimits.DocumentTypeMaxLength, required: false);
        RuleFor(x => x.Version).ValidName(LegalDocumentLimits.VersionMaxLength, required: false);
    }
}

public sealed class UpdateLegalDocumentVersionCommandValidator : LegalDocumentContentInputValidator<UpdateLegalDocumentVersionCommand> { }

public sealed class PublishLegalDocumentVersionCommandValidator : AbstractValidator<PublishLegalDocumentVersionCommand>
{
    public PublishLegalDocumentVersionCommandValidator()
    {
        RuleFor(x => x.PublishReason).ValidName(LegalDocumentLimits.PublishReasonMaxLength, required: false);
    }
}
