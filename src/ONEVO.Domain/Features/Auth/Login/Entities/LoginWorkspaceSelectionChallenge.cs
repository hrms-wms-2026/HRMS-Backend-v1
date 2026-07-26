using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Durable server-side authority for base-domain credential-first login when the same
/// normalized email and password match active users in more than one login-eligible tenant.
/// Created only after password verification succeeds for multiple candidates. Not tenant-owned:
/// this row exists before a tenant is resolved, so it carries no tenant_id and RLS does not apply.
/// </summary>
public class LoginWorkspaceSelectionChallenge
{
    public Guid Id { get; set; }
    public string ChallengeHash { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string CandidateWorkspacesJson { get; set; } = string.Empty;
    public string Purpose { get; set; } = "workspace_selection";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
