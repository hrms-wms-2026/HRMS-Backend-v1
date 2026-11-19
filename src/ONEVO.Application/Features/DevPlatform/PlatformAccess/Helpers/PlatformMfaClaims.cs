namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

/// <summary>
/// Claims describing a platform admin's MFA state. AdminDatabaseTicketStore rebuilds them from the
/// database on every request, so enrolling (or an admin reset) takes effect on the next request.
/// </summary>
public static class PlatformMfaClaims
{
    /// <summary>"true" when the admin has a confirmed TOTP enrollment, otherwise "false".</summary>
    public const string EnrolledClaimType = "platform_mfa_enrolled";

    public const string True = "true";
    public const string False = "false";
}
