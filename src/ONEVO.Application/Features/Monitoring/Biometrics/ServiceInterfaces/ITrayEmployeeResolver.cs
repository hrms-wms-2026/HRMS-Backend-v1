namespace ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

public sealed record EmployeeIdentity(
    Guid EmployeeId,
    string EmployeeNumber,
    string DisplayName,
    string Email);

/// <summary>
/// Resolves the real CoreHR employee record for a tray-device JWT identity.
/// Callers must already be running inside the correct tenant context.
/// </summary>
public interface ITrayEmployeeResolver
{
    Task<EmployeeIdentity?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
