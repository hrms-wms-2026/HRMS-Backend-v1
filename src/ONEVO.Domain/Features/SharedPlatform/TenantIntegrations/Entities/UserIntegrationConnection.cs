using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

public sealed class UserIntegrationConnection : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string IntegrationKey { get; set; } = string.Empty;
    public string? ProviderUserId { get; set; }
    public string? ProviderUsername { get; set; }
    public string? ProviderEmail { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public string[]? ScopesGranted { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
