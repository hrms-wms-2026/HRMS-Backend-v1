namespace ONEVO.Api.Errors;

/// <summary>
/// RFC 9457 problem "type" values. A URN rather than a URL, so no domain is implied.
/// The web app matches some of these exactly (for example "urn:crownus:error:csrf" in
/// error.interceptor.ts), so a change here must ship with it.
/// </summary>
public static class ProblemTypes
{
    public static string For(string name) => $"urn:crownus:error:{name}";
}
