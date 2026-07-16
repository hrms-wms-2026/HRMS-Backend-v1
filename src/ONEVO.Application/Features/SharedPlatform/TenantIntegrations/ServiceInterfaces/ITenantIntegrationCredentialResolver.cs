namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

public sealed record ResolvedTenantIntegrationCredential(
    Guid TenantId, string IntegrationKey, string? AccessToken, string? RefreshToken,
    DateTimeOffset? TokenExpiresAt, string[] ScopesGranted,
    string? ExternalAccountId, string? ExternalAccountName);

public interface ITenantIntegrationCredentialResolver
{
    Task<ResolvedTenantIntegrationCredential?> GetConnectedCredentialAsync(
        Guid tenantId, string integrationKey, CancellationToken ct);
}
