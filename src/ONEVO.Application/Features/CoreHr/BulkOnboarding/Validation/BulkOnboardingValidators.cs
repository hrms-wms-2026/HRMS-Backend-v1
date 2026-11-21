using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ResolveBulkOnboardingIssues;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Validation;

/// <summary>
/// Task 19b. bulk_onboarding_batches: original_file_name nvarchar(255), default_employment_type nvarchar(30). A batch
/// holds at most <see cref="CsvBatchParser.MaxRows"/> rows. The column mapping goes from a file header to a field
/// name; 200 columns, 255-character headers and 100-character field names are far above any real template. An
/// issue key is the issue type plus the imported cell value, and a fix's new value is a cell value; 1,000 characters
/// is above every draft column they land in (the widest is 500). The created department and position use the
/// department (name 100, code 20) and position (name 100, code 5) limits. The handlers keep the known actions,
/// fields, required values and the business rules.
/// </summary>
public static class BulkOnboardingLimits
{
    public const int EmploymentTypeMaxLength = 30;
    public const int MaxMappedColumns = 200;
    public const int HeaderMaxLength = 255;
    public const int FieldNameMaxLength = 100;
    public const int CellValueMaxLength = 1_000;
    public const int IssueKeyMaxLength = 1_100;
    public const int DepartmentNameMaxLength = 100;
    public const int DepartmentCodeMaxLength = 20;
    public const int PositionNameMaxLength = 100;
    public const int PositionCodeMaxLength = 5;
}

public sealed class PreviewBulkOnboardingMappingCommandValidator : AbstractValidator<PreviewBulkOnboardingMappingCommand>
{
    public PreviewBulkOnboardingMappingCommandValidator()
    {
        RuleFor(x => x.Mapping).ValidNullableStringMap(
            BulkOnboardingLimits.MaxMappedColumns, BulkOnboardingLimits.HeaderMaxLength, BulkOnboardingLimits.FieldNameMaxLength);
    }
}

public sealed class ValidateBulkOnboardingBatchCommandValidator : AbstractValidator<ValidateBulkOnboardingBatchCommand>
{
    public ValidateBulkOnboardingBatchCommandValidator()
    {
        RuleFor(x => x.Mapping).ValidNullableStringMap(
            BulkOnboardingLimits.MaxMappedColumns, BulkOnboardingLimits.HeaderMaxLength, BulkOnboardingLimits.FieldNameMaxLength);
    }
}

public sealed class UploadBulkOnboardingBatchCommandValidator : AbstractValidator<UploadBulkOnboardingBatchCommand>
{
    public UploadBulkOnboardingBatchCommandValidator()
    {
        RuleFor(x => x.OriginalFileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.DefaultEmploymentType).ValidName(BulkOnboardingLimits.EmploymentTypeMaxLength, required: false);
    }
}

public sealed class ResolveBulkOnboardingIssuesCommandValidator : AbstractValidator<ResolveBulkOnboardingIssuesCommand>
{
    public ResolveBulkOnboardingIssuesCommandValidator()
    {
        RuleFor(x => x.IssueKey).ValidName(BulkOnboardingLimits.IssueKeyMaxLength, required: false);
        RuleFor(x => x.Action).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.TargetId).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.NewValue).ValidName(BulkOnboardingLimits.CellValueMaxLength, required: false);
        RuleFor(x => x.ApplyToRowNumbers)
            .Must(rows => rows is null || rows.Count <= CsvBatchParser.MaxRows)
            .WithMessage($"'{{PropertyName}}' can hold at most {CsvBatchParser.MaxRows} entries.");
        When(x => x.CreateDepartment is not null, () =>
        {
            RuleFor(x => x.CreateDepartment!.Name).ValidName(BulkOnboardingLimits.DepartmentNameMaxLength, required: false);
            RuleFor(x => x.CreateDepartment!.Code).ValidName(BulkOnboardingLimits.DepartmentCodeMaxLength, required: false);
        });
        When(x => x.CreatePosition is not null, () =>
        {
            RuleFor(x => x.CreatePosition!.Name).ValidName(BulkOnboardingLimits.PositionNameMaxLength, required: false);
            RuleFor(x => x.CreatePosition!.Code).ValidName(BulkOnboardingLimits.PositionCodeMaxLength, required: false);
        });
    }
}
