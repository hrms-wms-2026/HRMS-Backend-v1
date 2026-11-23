using FluentValidation.TestHelper;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: legal document versions.</summary>
public sealed class LegalDocumentValidatorTests
{
    private static CreateLegalDocumentVersionCommand Create(
        string type = "terms_of_service", string version = "1.0", string title = "Terms", string json = "{}",
        string html = "<p>Terms</p>", string text = "Terms", string scope = "login") =>
        new(type, version, title, json, html, text, true, scope, Guid.NewGuid());

    [Fact]
    public void Create_AtLimits_Passes()
        => new CreateLegalDocumentVersionCommandValidator()
            .TestValidate(Create(S(80), S(50), S(200), S(1_000_000), S(500_000), S(250_000), S(40)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_OverLimits_Fails()
    {
        var result = new CreateLegalDocumentVersionCommandValidator()
            .TestValidate(Create(S(81), S(51), S(201), S(1_000_001), S(500_001), S(250_001), S(41)));
        result.ShouldHaveValidationErrorFor(x => x.DocumentType);
        result.ShouldHaveValidationErrorFor(x => x.Version);
        result.ShouldHaveValidationErrorFor(x => x.Title);
        result.ShouldHaveValidationErrorFor(x => x.ContentJson);
        result.ShouldHaveValidationErrorFor(x => x.ContentHtml);
        result.ShouldHaveValidationErrorFor(x => x.ContentText);
        result.ShouldHaveValidationErrorFor(x => x.BlockScope);
    }

    [Fact]
    public void Update_ActiveContentInHtml_Fails()
    {
        var validator = new UpdateLegalDocumentVersionCommandValidator();
        validator.TestValidate(new UpdateLegalDocumentVersionCommand(Guid.NewGuid(), "Terms", "{}", "<p>ok</p>", "ok", true, "login"))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new UpdateLegalDocumentVersionCommand(Guid.NewGuid(), "Terms", "{}", Script, "ok", true, "login"))
            .ShouldHaveValidationErrorFor(x => x.ContentHtml);
    }

    [Fact]
    public void Publish_ReasonUpTo2000_Passes_Longer_Fails()
    {
        var validator = new PublishLegalDocumentVersionCommandValidator();
        validator.TestValidate(new PublishLegalDocumentVersionCommand(Guid.NewGuid(), null, Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new PublishLegalDocumentVersionCommand(Guid.NewGuid(), S(2_000), Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new PublishLegalDocumentVersionCommand(Guid.NewGuid(), S(2_001), Guid.NewGuid()))
            .ShouldHaveValidationErrorFor(x => x.PublishReason);
    }
}
