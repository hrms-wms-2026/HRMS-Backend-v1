using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// How a "fully authenticated, every gate cleared" outcome must be finalized. Deliberately
/// explicit and caller-supplied - LoginContinuationService never infers this from a host string or
/// ITenantContext itself; each caller already knows which host it runs on (either structurally,
/// e.g. BaseLoginCommandHandler is base-host-only, or via its own ITenantContext.ContextMode guard,
/// e.g. AcceptInvitationPasswordCommandHandler requires TenantContextMode.Tenant) and must say so.
/// </summary>
public enum LoginFinalizationMode
{
    /// <summary>
    /// Caller runs on the base host (or a pre-tenant challenge continuation of one). The base host
    /// must never set onevo_session/onevo_csrf, so this issues a one-time tenant session exchange
    /// code/continue_url instead (TenantSessionExchangeService.CreateAsync).
    /// </summary>
    BaseDomainExchange,

    /// <summary>
    /// Caller already runs inside the correct tenant host's request (invite acceptance, forced
    /// password change) - there is no host to hand off to, so this signs in directly via
    /// ILoginSessionMaterialFactory, exactly as every login used to before the exchange flow.
    /// </summary>
    TenantHostDirect
}

/// <summary>
/// One tenant/user pair to continue logging in as, plus how to reach it. Callers that already run
/// inside the correct tenant's RLS context via host-resolved middleware (tenant-host login) pass
/// SwitchTenantContext=false; callers that resolved the tenant dynamically after a pre-tenant
/// lookup (base-domain credential/Google login, workspace selection) pass true.
/// </summary>
public sealed record LoginContinuationRequest(
    Guid TenantId,
    Guid UserId,
    bool SwitchTenantContext,
    string GenericFailureMessage,
    string LegalChallengeOrigin,
    string? IpAddress,
    string? UserAgent,
    LoginFinalizationMode FinalizationMode);

/// <summary>
/// Owns the post-resolution login continuation pipeline shared by every login entry point:
/// tenant re-validation (Active/Trial), tenant context switch when needed, user re-validation
/// (exists + IsActive), must-change-password short-circuit, per-user MFA challenge issuance,
/// required-legal-document blocking, session material creation, LastLoginAt update, and
/// SaveChanges. Callers that have already independently verified identity and just completed a
/// per-user gate (MFA verification) call <see cref="FinishAuthenticatedLoginAsync"/> directly to
/// run only the legal-check-and-session tail without repeating tenant/user resolution or the MFA
/// check itself.
/// </summary>
public interface ILoginContinuationService
{
    Task<Result<LoginResponseDto>> ContinueAsync(
        LoginContinuationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// <paramref name="tenant"/> is optional: pass it when the caller already has the Tenant loaded
    /// (avoids a redundant lookup); omit it and this will fetch <c>user.TenantId</c> itself.
    /// </summary>
    Task<Result<LoginResponseDto>> FinishAuthenticatedLoginAsync(
        User user,
        string legalChallengeOrigin,
        string? ipAddress,
        string? userAgent,
        LoginFinalizationMode finalizationMode,
        CancellationToken ct = default,
        Tenant? tenant = null);
}
