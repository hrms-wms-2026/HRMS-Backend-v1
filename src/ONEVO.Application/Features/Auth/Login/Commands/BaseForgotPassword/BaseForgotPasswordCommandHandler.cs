using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;

/// <summary>
/// Base-domain (root host) forgot-password: tenant context is not yet resolved, so eligibility is
/// looked up the same way base-domain login resolves candidates - via the allowlisted
/// auth_lookup_base_login_candidates function (IBaseLoginCandidateRepository), never by querying
/// users/tenants directly. One user's email can be eligible in more than one tenant; each eligible
/// tenant/user gets its own password_reset_token and its own tenant-bound reset email, enqueued
/// through the transactional outbox so a click never has to guess (or be told) which workspace it
/// belongs to, and so delivery survives a transient email-provider outage.
///
/// password_reset_tokens and outbox_messages are tenant-owned tables protected by RLS. The request
/// starts in root/system tenant context (no tenant is resolved yet on the base domain), so writes
/// to either table must not happen until this handler switches into the candidate's own tenant
/// context via ITenantContextSwitcher - mirroring SelectWorkspaceCommandHandler's existing
/// post-base-login tenant switch. Each candidate is switched into, written for, and saved
/// independently: there is no single cross-tenant SaveChangesAsync, because RLS only allows the
/// currently-active session tenant's rows through on each write.
///
/// The lookup function returns at most nine rows: an email eligible in nine or more tenants
/// produces a ninth "overflow probe" row that is not a real candidate (see
/// IBaseLoginCandidateRepository). Base-login's fixed-work verifier already treats a 9th row as
/// overflow-only, never real; this handler applies the identical Count > 8 rule so an email with
/// more than eight eligible tenants gets no tokens and no emails at all, rather than silently
/// serving 8 of N and treating the probe as real. Overflow is checked before any tenant switch or
/// write, so it never touches another tenant's context at all.
/// </summary>
public class BaseForgotPasswordCommandHandler : IRequestHandler<BaseForgotPasswordCommand, Result>
{
    private const int MaxEligibleCandidates = 8;

    private readonly IBaseLoginCandidateRepository _candidates;
    private readonly IPasswordResetTokenRepository _passwordResetTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecureTokenGenerator _tokenService;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ILogger<BaseForgotPasswordCommandHandler> _logger;

    public BaseForgotPasswordCommandHandler(
        IBaseLoginCandidateRepository candidates,
        IPasswordResetTokenRepository passwordResetTokens,
        IUnitOfWork unitOfWork,
        ISecureTokenGenerator tokenService,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        ITenantContextSwitcher tenantSwitcher,
        ILogger<BaseForgotPasswordCommandHandler> logger)
    {
        _candidates = candidates;
        _passwordResetTokens = passwordResetTokens;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _outbox = outbox;
        _clock = clock;
        _tenantSwitcher = tenantSwitcher;
        _logger = logger;
    }

    public async Task<Result> Handle(BaseForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var candidateRows = await _candidates.GetCandidatesAsync(normalizedEmail, cancellationToken);

        // Overflow: the 9th row is only a probe proving "more than 8 eligible tenants exist", not
        // a real candidate. Issuing tokens for 8 of them (and silently dropping the rest) would
        // both leak the overflow condition through side effects and abandon real tenants. Treat
        // the whole request as a no-op instead - same generic 200 the controller always returns.
        if (candidateRows.Count > MaxEligibleCandidates)
        {
            _logger.LogWarning(
                "Base-domain forgot-password candidate overflow for normalized email hash {EmailHash}.",
                Fingerprint(normalizedEmail));
            return Result.Success();
        }

        foreach (var candidate in candidateRows)
        {
            // TenantStatus.Active is a placeholder, not an assumption: ITenantContext.Status is
            // never read by TenantRlsInterceptor (which gates the RLS session GUCs on TenantId +
            // ContextMode only) or by any repository in the write path below, so its exact value
            // cannot affect which rows RLS admits. auth_lookup_base_login_candidates itself already
            // filters to t.status IN ('Active', 'Trial') (see
            // AddAuthLookupBaseLoginCandidatesFunction), so every row reaching this loop is already
            // known login-eligible; BaseLoginCandidateRow deliberately doesn't carry the exact
            // status (see IBaseLoginCandidateRepository), and widening the allowlisted function to
            // add it would be scope creep with no behavioral effect here.
            await _tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(candidate.TenantId, candidate.Slug, TenantStatus.Active, null),
                cancellationToken);

            var existing = await _passwordResetTokens.ListValidByUserIdAsync(
                candidate.UserId, _clock.UtcNow, cancellationToken);

            foreach (var t in existing)
                t.UsedAt = _clock.UtcNow; // Mark as used/invalidated

            var rawToken = _tokenService.GenerateOpaqueToken(); // Random opaque token
            var tokenHash = _tokenService.HashToken(rawToken);

            await _passwordResetTokens.AddAsync(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = candidate.UserId,
                TenantId = candidate.TenantId,
                TokenHash = tokenHash,
                ExpiresAt = _clock.UtcNow.AddHours(1),
                CreatedAt = _clock.UtcNow
            }, cancellationToken);

            // IBaseLoginCandidateRepository intentionally does not return the user's email - the
            // allowlisted lookup function returns only what base-domain login needs to display a
            // workspace picker, and it previously avoided returning anything extra. The submitted
            // normalizedEmail already matched this candidate's normalized_email in that lookup, so
            // it is the correct send-to address without widening the function's output.
            await _outbox.EnqueueAsync(
                OutboxMessageTypes.PasswordResetEmail,
                new PasswordResetEmailPayload(
                    candidate.TenantId, candidate.UserId, normalizedEmail, rawToken, candidate.Slug),
                tenantId: candidate.TenantId,
                cancellationToken);

            // Each candidate is an independent tenant-scoped unit of work: the token and its
            // outbox row commit together while this candidate's tenant context is still active, so
            // RLS admits both writes. They must not be batched into one cross-tenant
            // SaveChangesAsync, since only the currently-active session tenant's rows would pass
            // RLS on a shared save.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private static string Fingerprint(string normalizedEmail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));
}
