using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

public sealed class TenantIntegrationCredential : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IntegrationKey { get; set; } = string.Empty;
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public string[] ScopesGranted { get; set; } = Array.Empty<string>();
    public string? ExternalAccountId { get; set; }
    public string? ExternalAccountName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public Guid ConnectedByUserId { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
}
