using Microsoft.AspNetCore.Authorization;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Auth;

/// <summary>
/// Authorization policies for the Developer Platform admin cookie session (AdminScheme).
///
/// <see cref="Admin"/> is the policy on every admin/v1 route. Besides an authenticated admin it
/// requires a confirmed MFA enrollment (F-02, security audit 2026-11-05): admin sessions run in RLS
/// admin mode across every tenant, so a password alone must never reach them. The enrollment claim
/// is rebuilt from the database on every request by AdminDatabaseTicketStore, so the restriction
/// lifts on the next request after mfa/confirm-setup, with no new login.
///
/// <see cref="MfaEnrollment"/> only needs an authenticated admin. It is used by exactly the four
/// endpoints a not-yet-enrolled admin needs: mfa/enable, mfa/confirm-setup, me and logout. A unit
/// test pins that list; do not add routes to it.
/// </summary>
public static class AdminAuthorizationPolicies
{
    public const string Admin = "AdminPolicy";
    public const string MfaEnrollment = "AdminMfaEnrollmentPolicy";

    private const string Scheme = "AdminScheme";

    public static void Register(AuthorizationOptions options)
    {
        // Endpoint-level authorization stays permission-based via [RequirePlatformPermission]
        // (platform permission codes resolved from the database); role names are never
        // authorization rules per the access-control contract.
        options.AddPolicy(Admin, policy =>
            policy.AddAuthenticationSchemes(Scheme)
                  .RequireAuthenticatedUser()
                  .RequireClaim("platform_role")
                  .RequireClaim(PlatformMfaClaims.EnrolledClaimType, PlatformMfaClaims.True));

        options.AddPolicy(MfaEnrollment, policy =>
            policy.AddAuthenticationSchemes(Scheme)
                  .RequireAuthenticatedUser()
                  .RequireClaim("platform_role"));
    }

    /// <summary>
    /// True when an authenticated admin principal lacks a confirmed MFA enrollment, which is the
    /// reason AdminPolicy refused it. Used to explain the 403 to the admin console.
    /// </summary>
    public static bool IsAwaitingMfaEnrollment(System.Security.Claims.ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
        && !user.HasClaim(PlatformMfaClaims.EnrolledClaimType, PlatformMfaClaims.True);
}
