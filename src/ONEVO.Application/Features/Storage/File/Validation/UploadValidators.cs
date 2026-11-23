using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo;
using ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;

namespace ONEVO.Application.Features.Storage.File.Validation;

/// <summary>
/// Task 19b. Every upload stores its original name and content type in file_records (original_file_name
/// nvarchar(255), content_type nvarchar(150)). The purpose is only compared with UploadPurposeCatalog. Missing
/// names, unknown purposes, allowed types, signatures and sizes stay with the upload purpose policy, which answers
/// with its own 400.
/// </summary>
public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileCommandValidator()
    {
        RuleFor(x => x.Purpose).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.FileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

public sealed class CreateTaskPendingUploadCommandValidator : AbstractValidator<CreateTaskPendingUploadCommand>
{
    public CreateTaskPendingUploadCommandValidator()
    {
        RuleFor(x => x.Purpose).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.OriginalFileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

public sealed class UploadObjectiveAssetCommandValidator : AbstractValidator<UploadObjectiveAssetCommand>
{
    public UploadObjectiveAssetCommandValidator()
    {
        RuleFor(x => x.OriginalFileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

public sealed class UploadLeaveDocumentCommandValidator : AbstractValidator<UploadLeaveDocumentCommand>
{
    public UploadLeaveDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

/// <summary>The tray sends the screenshot's name and type; both are stored with the file record.</summary>
public sealed class SubmitPeriodicScreenshotCommandValidator : AbstractValidator<SubmitPeriodicScreenshotCommand>
{
    public SubmitPeriodicScreenshotCommandValidator()
    {
        RuleFor(x => x.FileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

/// <summary>global_applications.logo_content_type is nvarchar(100).</summary>
public sealed class SetGlobalApplicationLogoCommandValidator : AbstractValidator<SetGlobalApplicationLogoCommand>
{
    public const int LogoContentTypeMaxLength = 100;

    public SetGlobalApplicationLogoCommandValidator()
    {
        RuleFor(x => x.ContentType).ValidName(LogoContentTypeMaxLength, required: false);
    }
}

/// <summary>The handler checks the document type against EmployeeDocumentTypes and the trimmed name against its
/// own 150-character limit; this bounds the raw values first.</summary>
public sealed class AddMyDocumentCommandValidator : AbstractValidator<AddMyDocumentCommand>
{
    public AddMyDocumentCommandValidator()
    {
        RuleFor(x => x.DocumentType).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.Name).ValidName(AddMyDocumentCommandHandler.MaxNameLength, required: false);
    }
}
