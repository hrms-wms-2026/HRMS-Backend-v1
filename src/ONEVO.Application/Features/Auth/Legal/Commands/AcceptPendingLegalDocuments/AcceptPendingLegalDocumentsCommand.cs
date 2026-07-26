using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;

/// <summary>
/// Completes the pre-session Legal &amp; Privacy flow using the opaque, single-use
/// legal_login_challenges handle (never a tenant_id/user_id supplied by the browser). The
/// LegalCsrfToken is the readable double-submit value returned alongside the challenge; it is
/// validated against the challenge's own csrf_token_hash, independent of any session cookie.
/// </summary>
public record AcceptPendingLegalDocumentsCommand(
    string LegalChallenge,
    string LegalCsrfToken,
    IReadOnlyList<LegalAcceptanceItemInput> Acceptances,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<LoginResponseDto>>;
