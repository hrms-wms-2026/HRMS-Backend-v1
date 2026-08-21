using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public sealed record RowValidationError(
    string Code,
    string Field,
    string Message,
    string? ImportedValue,
    string? RelatedEntityId = null);

public sealed record RowValidationOutcome(
    bool IsValid,
    RowValidationError? Error,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? TemplateId,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly? StartDate,
    string EmploymentType,
    int? WorkModeId,
    string? EmployeeNumber,
    Guid? ReportsToEmployeeId)
{
    public string? ErrorMessage => Error?.Message;
}

public interface IBulkOnboardingRowValidator
{
    Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId,
        BulkOnboardingBatch batch,
        Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping,
        ISet<string> emailsSeenInThisFile,
        CancellationToken ct,
        Models.BulkOnboardingResolutionState? resolutionState = null);
}
