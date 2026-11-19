namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

public sealed record OfficeProjectContext(
    Guid ProjectId,
    Guid ObjectiveId,
    Guid ToDoStatusId,
    IReadOnlyDictionary<string, Guid> CategoryIds);

/// <summary>
/// Creates or loads the tenant's one system-managed Office project. Implemented in Infrastructure
/// because losing the unique-key race must clear and reload the EF change tracker safely.
/// </summary>
public interface IOfficeProjectProvisioner
{
    Task<OfficeProjectContext> EnsureAsync(
        Guid tenantId,
        Guid initialOwningLegalEntityId,
        Guid actingUserId,
        Guid actingEmployeeId,
        DateOnly targetDate,
        CancellationToken ct = default);
}
