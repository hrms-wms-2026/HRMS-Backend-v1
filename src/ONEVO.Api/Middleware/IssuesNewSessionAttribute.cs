namespace ONEVO.Api.Middleware;

/// <summary>
/// Marks an endpoint that can sign the caller in (create a brand-new cookie session) for
/// <see cref="Scheme"/>. <see cref="NewSessionSignInMiddleware"/> keeps the cookie handler from
/// ever seeing the caller's existing session cookie on such requests - see that class for why.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IssuesNewSessionAttribute : Attribute
{
    public IssuesNewSessionAttribute(string scheme) => Scheme = scheme;

    /// <summary>"TenantScheme" or "AdminScheme".</summary>
    public string Scheme { get; }
}
