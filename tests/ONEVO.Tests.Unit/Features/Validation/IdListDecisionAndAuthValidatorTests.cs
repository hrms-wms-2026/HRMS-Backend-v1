using FluentValidation.TestHelper;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaConfirmSetup;
using ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;
using ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;
using ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;
using ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventGuests;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;
using static ONEVO.Tests.Unit.Features.Validation.ValidationRuleExtensionsTests;

namespace ONEVO.Tests.Unit.Features.Validation;

/// <summary>Task 19: id lists from the request, approver decision notes, and auth inputs.</summary>
public sealed class IdListDecisionAndAuthValidatorTests
{
    private const string Challenge = "q8y0hJ7sV2m3Xo1c4Zb5Nn6Aa7Kk8Ll9Mm0Pp1Qq2Rr3Ss4Tt5Uu6Vv7Ww8Xx9Yy0Zz1Aa2Bb3Cc4Dd5Ee6Ff7Gg8Hh==";

    // ---- Id lists ----

    [Fact]
    public void BulkOnboardingFinalize_UpTo200Drafts_Passes_More_Fails()
    {
        var validator = new RequestBulkOnboardingFinalizeCommandValidator();
        validator.TestValidate(new RequestBulkOnboardingFinalizeCommand(Guid.NewGuid(), Ids(200))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RequestBulkOnboardingFinalizeCommand(Guid.NewGuid(), Ids(201)))
            .ShouldHaveValidationErrorFor(x => x.OnboardingDraftIds);
    }

    [Fact]
    public void SetSprintTasks_UpTo500PerList_Passes_More_Fails()
    {
        var validator = new SetSprintTasksCommandValidator();
        validator.TestValidate(new SetSprintTasksCommand(Guid.NewGuid(), Ids(500), Ids(500))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new SetSprintTasksCommand(Guid.NewGuid(), Ids(501), Ids(501)));
        result.ShouldHaveValidationErrorFor(x => x.AddTaskIds);
        result.ShouldHaveValidationErrorFor(x => x.RemoveTaskIds);
    }

    [Fact]
    public void AddCalendarGuests_UpTo20_Passes()
        => new AddCalendarEventGuestsCommandValidator()
            .TestValidate(new AddCalendarEventGuestsCommand(Guid.NewGuid(), Enumerable.Range(0, 20).Select(i => $"g{i}@example.com").ToList()))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AddCalendarGuests_MoreThan20OrLongEmail_Fails()
    {
        var validator = new AddCalendarEventGuestsCommandValidator();
        validator.TestValidate(new AddCalendarEventGuestsCommand(Guid.NewGuid(), Enumerable.Range(0, 21).Select(i => $"g{i}@example.com").ToList()))
            .ShouldHaveValidationErrorFor(x => x.Emails);
        validator.TestValidate(new AddCalendarEventGuestsCommand(Guid.NewGuid(), [new string('a', 250) + "@x.com"]))
            .ShouldHaveValidationErrorFor("Emails[0]");
    }

    // ---- Decision notes ----

    [Fact]
    public void CreateBypassRequest_RequiresReason_AndBoundsBothTexts()
    {
        var validator = new CreateBypassRequestCommandValidator();
        validator.TestValidate(new CreateBypassRequestCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Laptop already returned", null))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateBypassRequestCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", null))
            .ShouldHaveValidationErrorFor(x => x.BypassReason);
        var result = validator.TestValidate(new CreateBypassRequestCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('r', 501), new string('p', 501)));
        result.ShouldHaveValidationErrorFor(x => x.BypassReason);
        result.ShouldHaveValidationErrorFor(x => x.PenaltyDescription);
    }

    [Fact]
    public void RejectBypassRequest_CommentUpTo500_Passes_Longer_Fails()
    {
        var validator = new RejectBypassRequestCommandValidator();
        validator.TestValidate(new RejectBypassRequestCommand(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RejectBypassRequestCommand(Guid.NewGuid(), new string('c', 500))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RejectBypassRequestCommand(Guid.NewGuid(), new string('c', 501)))
            .ShouldHaveValidationErrorFor(x => x.DecisionComment);
    }

    [Fact]
    public void RejectAccessGrantRequest_NoteUpTo500_Passes_Longer_Fails()
    {
        var validator = new RejectAccessGrantRequestCommandValidator();
        validator.TestValidate(new RejectAccessGrantRequestCommand(Guid.NewGuid(), new string('n', 500))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RejectAccessGrantRequestCommand(Guid.NewGuid(), new string('n', 501)))
            .ShouldHaveValidationErrorFor(x => x.DecisionNote);
    }

    // ---- MFA ----

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("+12345")]
    [InlineData("")]
    public void MfaCodes_ThatAreNotSixDigits_Fail(string code)
    {
        new VerifyMfaCommandValidator().TestValidate(new VerifyMfaCommand(Challenge, code, null, null))
            .ShouldHaveValidationErrorFor(x => x.Code);
        new VerifyAdminMfaCommandValidator().TestValidate(new VerifyAdminMfaCommand(Challenge, code))
            .ShouldHaveValidationErrorFor(x => x.Code);
        new ConfirmAdminMfaSetupCommandValidator().TestValidate(new ConfirmAdminMfaSetupCommand(code))
            .ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void MfaCodes_SixDigits_Pass()
    {
        new VerifyMfaCommandValidator().TestValidate(new VerifyMfaCommand(Challenge, "012345", "203.0.113.7", "Mozilla/5.0"))
            .ShouldNotHaveAnyValidationErrors();
        new VerifyAdminMfaCommandValidator().TestValidate(new VerifyAdminMfaCommand(Challenge, "987654")).ShouldNotHaveAnyValidationErrors();
        new ConfirmAdminMfaSetupCommandValidator().TestValidate(new ConfirmAdminMfaSetupCommand("000000")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void VerifyMfa_OversizedChallengeIpOrUserAgent_Fails()
    {
        var result = new VerifyMfaCommandValidator().TestValidate(
            new VerifyMfaCommand(new string('c', 513), "123456", new string('1', 46), new string('u', 501)));
        result.ShouldHaveValidationErrorFor(x => x.MfaChallenge);
        result.ShouldHaveValidationErrorFor(x => x.IpAddress);
        result.ShouldHaveValidationErrorFor(x => x.UserAgent);
        new VerifyAdminMfaCommandValidator().TestValidate(new VerifyAdminMfaCommand(new string('c', 513), "123456"))
            .ShouldHaveValidationErrorFor(x => x.Challenge);
    }

    // ---- Password reset ----

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void ForgotPassword_BadEmail_Fails(string email)
    {
        new RequestPasswordResetCommandValidator().TestValidate(new RequestPasswordResetCommand(email))
            .ShouldHaveValidationErrorFor(x => x.Email);
        new BaseForgotPasswordCommandValidator().TestValidate(new BaseForgotPasswordCommand(email))
            .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void ForgotPassword_EmailOver255_Fails()
    {
        var email = new string('a', 250) + "@x.com";
        new RequestPasswordResetCommandValidator().TestValidate(new RequestPasswordResetCommand(email)).ShouldHaveValidationErrorFor(x => x.Email);
        new BaseForgotPasswordCommandValidator().TestValidate(new BaseForgotPasswordCommand(email)).ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void ForgotPassword_UnknownButWellFormedEmail_Passes()
    {
        new RequestPasswordResetCommandValidator().TestValidate(new RequestPasswordResetCommand("nobody@test.onevo.dev"))
            .ShouldNotHaveAnyValidationErrors();
        new BaseForgotPasswordCommandValidator().TestValidate(new BaseForgotPasswordCommand("Nobody@Test.Onevo.Dev"))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- Legal acceptance ----

    [Fact]
    public void LegalAcceptance_Valid_Passes()
        => new SubmitLegalAcceptanceCommandValidator()
            .TestValidate(new SubmitLegalAcceptanceCommand(
                [new LegalAcceptanceItemInput("terms_of_service", "2026.1", "accepted", new string('a', 64))], "203.0.113.7", "Mozilla/5.0"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void LegalAcceptance_TooManyItems_Fails()
        => new SubmitLegalAcceptanceCommandValidator()
            .TestValidate(new SubmitLegalAcceptanceCommand(
                Enumerable.Range(0, 21).Select(i => new LegalAcceptanceItemInput("doc" + i, "1", "accepted")).ToList(), null, null))
            .ShouldHaveValidationErrorFor(x => x.Acceptances);

    [Fact]
    public void LegalAcceptance_OversizedItemFieldsOrHeaders_Fail()
    {
        var result = new SubmitLegalAcceptanceCommandValidator().TestValidate(new SubmitLegalAcceptanceCommand(
            [new LegalAcceptanceItemInput(new string('d', 81), new string('v', 51), new string('x', 21), new string('h', 129))],
            new string('1', 46), new string('u', 501)));
        result.ShouldHaveValidationErrorFor("Acceptances[0].DocumentType");
        result.ShouldHaveValidationErrorFor("Acceptances[0].Version");
        result.ShouldHaveValidationErrorFor("Acceptances[0].Decision");
        result.ShouldHaveValidationErrorFor("Acceptances[0].ContentHash");
        result.ShouldHaveValidationErrorFor(x => x.IpAddress);
        result.ShouldHaveValidationErrorFor(x => x.UserAgent);
    }

    [Fact]
    public void LegalAcceptance_EmptyList_IsLeftToTheHandler()
        => new SubmitLegalAcceptanceCommandValidator().TestValidate(new SubmitLegalAcceptanceCommand([], null, null))
            .ShouldNotHaveAnyValidationErrors();
}
