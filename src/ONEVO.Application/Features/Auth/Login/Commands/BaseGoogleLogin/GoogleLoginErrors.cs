namespace ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;

/// <summary>
/// The only two failures tenant Google sign-in reports. Google failures are 401; every reason the
/// application refuses a verified Google identity is the same 403, so the response never reveals
/// whether the account exists, is inactive, or which tenant policy blocked it.
/// </summary>
public static class GoogleLoginErrors
{
    public const string GoogleAuthFailedCode = "google_auth_failed";
    public const string GoogleAuthFailedMessage = "Google sign-in failed. Please try again.";

    public const string NoAccessCode = "no_access";
    public const string NoAccessMessage =
        "This Google account does not have access to Crownus. Ask your administrator for an invitation.";
}
