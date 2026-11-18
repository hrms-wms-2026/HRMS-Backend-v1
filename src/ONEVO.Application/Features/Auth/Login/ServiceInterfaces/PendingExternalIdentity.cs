namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// A verified external identity (Google "sub") that started a login and is carried through the
/// workspace-selection and MFA steps. It is written to user_external_identities only when the login
/// fully completes (LoginContinuationService.FinishAuthenticatedLoginAsync, after the legal check):
/// a new link when the user was found by email, or a LastUsedAt update when found by subject.
/// Never contains a token.
/// </summary>
public sealed record PendingExternalIdentity(
    string Provider,
    string Subject,
    string Email,
    bool MatchedBySubject);
