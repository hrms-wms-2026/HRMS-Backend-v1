using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public sealed record RowValidationOutcome(
    bool IsValid, string? ErrorMessage,
    Guid? DepartmentId, Guid? PositionId, Guid? TemplateId,
    string FirstName, string LastName, string WorkEmail, DateOnly? StartDate,
    string EmploymentType, int? WorkModeId, string? EmployeeNumber);

public interface IBulkOnboardingRowValidator
{
    Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId,
        BulkOnboardingBatch batch,
        Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping,
        ISet<string> emailsSeenInThisFile,
        CancellationToken ct);
}
