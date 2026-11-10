namespace ONEVO.Api.Auth;

/// <summary>
/// Browser cookie names. The web app (`src/app/core/auth/auth-cookie-names.ts`) and the tray
/// read the same names, so a change here must ship with both. Layers below the API that set or
/// read these cookies use the same literal values.
/// </summary>
public static class AuthCookieNames
{
    public const string Session = "crownus_session";
    public const string Csrf = "crownus_csrf";
    public const string Mfa = "crownus_mfa";
    public const string LegalPending = "crownus_legal_pending";
    public const string LegalCsrf = "crownus_legal_csrf";
    public const string LastTenant = "crownus_last_tenant";
}
