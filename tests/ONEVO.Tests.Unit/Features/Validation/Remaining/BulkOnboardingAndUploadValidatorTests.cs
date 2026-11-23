using FluentValidation.TestHelper;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ResolveBulkOnboardingIssues;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Validation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo;
using ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.Storage.File.Validation;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: bulk onboarding and file uploads.</summary>
public sealed class BulkOnboardingAndUploadValidatorTests
{
    // ---- Bulk onboarding ----

    [Fact]
    public void Mapping_AtLimits_Passes_OverLimits_Fails()
    {
        var ok = Enumerable.Range(0, 200).ToDictionary(i => $"{i}{S(250)}", i => (string?)(i == 0 ? null : S(100)));
        new PreviewBulkOnboardingMappingCommandValidator().TestValidate(new PreviewBulkOnboardingMappingCommand(Guid.NewGuid(), ok))
            .ShouldNotHaveAnyValidationErrors();

        var tooMany = Enumerable.Range(0, 201).ToDictionary(i => $"h{i}", _ => (string?)"first_name");
        new ValidateBulkOnboardingBatchCommandValidator().TestValidate(new ValidateBulkOnboardingBatchCommand(Guid.NewGuid(), tooMany))
            .ShouldHaveValidationErrorFor(x => x.Mapping);
        new ValidateBulkOnboardingBatchCommandValidator()
            .TestValidate(new ValidateBulkOnboardingBatchCommand(Guid.NewGuid(), new Dictionary<string, string?> { ["Name"] = S(101) }))
            .ShouldHaveValidationErrorFor(x => x.Mapping);
    }

    [Fact]
    public void UploadBatch_BoundsFileNameAndEmploymentType()
    {
        var validator = new UploadBulkOnboardingBatchCommandValidator();
        validator.TestValidate(new UploadBulkOnboardingBatchCommand("staff.csv", [], Guid.NewGuid(), null, "full_time", null))
            .ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new UploadBulkOnboardingBatchCommand(S(256), [], Guid.NewGuid(), null, S(31), null));
        result.ShouldHaveValidationErrorFor(x => x.OriginalFileName);
        result.ShouldHaveValidationErrorFor(x => x.DefaultEmploymentType);
    }

    [Fact]
    public void ResolveIssues_AtLimits_Passes()
        => new ResolveBulkOnboardingIssuesCommandValidator()
            .TestValidate(new ResolveBulkOnboardingIssuesCommand(Guid.NewGuid(), S(1_100), "create_department", S(100), S(1_000), null,
                Enumerable.Range(1, 200).ToList(), new ResolveBulkOnboardingCreateDepartment(S(100), S(20), null),
                new ResolveBulkOnboardingCreatePosition(Guid.NewGuid(), S(100), S(5), 1, null)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void ResolveIssues_OverLimits_Fails()
    {
        var result = new ResolveBulkOnboardingIssuesCommandValidator()
            .TestValidate(new ResolveBulkOnboardingIssuesCommand(Guid.NewGuid(), S(1_101), S(101), S(101), S(1_001), null,
                Enumerable.Range(1, 201).ToList(), new ResolveBulkOnboardingCreateDepartment(S(101), S(21), null),
                new ResolveBulkOnboardingCreatePosition(Guid.NewGuid(), S(101), S(6), 1, null)));
        foreach (var field in new[] { "IssueKey", "Action", "TargetId", "NewValue", "ApplyToRowNumbers",
                     "CreateDepartment.Name", "CreateDepartment.Code", "CreatePosition.Name", "CreatePosition.Code" })
            result.ShouldHaveValidationErrorFor(field);
    }

    // ---- Uploads ----

    [Fact]
    public void Uploads_AtLimits_Pass()
    {
        new UploadFileCommandValidator().TestValidate(new UploadFileCommand("avatar", S(255), S(150), EmptyStream())).ShouldNotHaveAnyValidationErrors();
        new CreateTaskPendingUploadCommandValidator().TestValidate(new CreateTaskPendingUploadCommand("task_attachment", "a.pdf", "application/pdf", EmptyStream()))
            .ShouldNotHaveAnyValidationErrors();
        new UploadObjectiveAssetCommandValidator().TestValidate(new UploadObjectiveAssetCommand(Guid.NewGuid(), "a.png", "image/png", EmptyStream()))
            .ShouldNotHaveAnyValidationErrors();
        new UploadLeaveDocumentCommandValidator().TestValidate(new UploadLeaveDocumentCommand("note.pdf", "application/pdf", EmptyStream()))
            .ShouldNotHaveAnyValidationErrors();
        new SubmitPeriodicScreenshotCommandValidator().TestValidate(new SubmitPeriodicScreenshotCommand("s.jpg", "image/jpeg", EmptyStream(), DateTimeOffset.UtcNow))
            .ShouldNotHaveAnyValidationErrors();
        new SetGlobalApplicationLogoCommandValidator().TestValidate(new SetGlobalApplicationLogoCommand(Guid.NewGuid(), [], S(100), Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
        new AddMyDocumentCommandValidator().TestValidate(new AddMyDocumentCommand(Guid.NewGuid(), "passport", S(150))).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Uploads_OverLimits_Fail()
    {
        var file = new UploadFileCommandValidator().TestValidate(new UploadFileCommand(S(101), S(256), S(151), EmptyStream()));
        file.ShouldHaveValidationErrorFor(x => x.Purpose);
        file.ShouldHaveValidationErrorFor(x => x.FileName);
        file.ShouldHaveValidationErrorFor(x => x.ContentType);

        new CreateTaskPendingUploadCommandValidator().TestValidate(new CreateTaskPendingUploadCommand("x", S(256), "a/b", EmptyStream()))
            .ShouldHaveValidationErrorFor(x => x.OriginalFileName);
        new UploadObjectiveAssetCommandValidator().TestValidate(new UploadObjectiveAssetCommand(Guid.NewGuid(), "a.png", S(151), EmptyStream()))
            .ShouldHaveValidationErrorFor(x => x.ContentType);
        new UploadLeaveDocumentCommandValidator().TestValidate(new UploadLeaveDocumentCommand(S(256), "a/b", EmptyStream()))
            .ShouldHaveValidationErrorFor(x => x.FileName);
        new SubmitPeriodicScreenshotCommandValidator().TestValidate(new SubmitPeriodicScreenshotCommand("s.jpg", S(151), EmptyStream(), DateTimeOffset.UtcNow))
            .ShouldHaveValidationErrorFor(x => x.ContentType);
        new SetGlobalApplicationLogoCommandValidator().TestValidate(new SetGlobalApplicationLogoCommand(Guid.NewGuid(), [], S(101), Guid.NewGuid()))
            .ShouldHaveValidationErrorFor(x => x.ContentType);
        var doc = new AddMyDocumentCommandValidator().TestValidate(new AddMyDocumentCommand(Guid.NewGuid(), S(101), S(151)));
        doc.ShouldHaveValidationErrorFor(x => x.DocumentType);
        doc.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
