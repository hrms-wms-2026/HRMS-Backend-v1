using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

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
    string? UserAgent);

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

    Task<Result<LoginResponseDto>> FinishAuthenticatedLoginAsync(
        User user,
        string legalChallengeOrigin,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
