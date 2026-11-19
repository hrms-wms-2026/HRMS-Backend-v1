using System.Net;
using Microsoft.Extensions.Configuration;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Task 05: outside Development/Test the API refuses to start while a setting a deployed environment needs is
/// empty, a placeholder (<c>&lt;...&gt;</c>, <c>CHANGE_ME</c>, <c>SET_VIA_ENV</c>) or still points at a local or
/// example host (<c>localhost</c>, <c>example.com</c>, <c>*.test</c>). All problems are reported together, by key
/// only - a message never contains a value. The full list of UAT settings is in
/// <c>docs/deployment/uat-configuration.md</c>.
/// </summary>
public static class DeploymentConfigurationValidator
{
    private const int MinimumEncryptionMasterKeyLength = 32;

    /// <summary>Plain required values (no host inside).</summary>
    private static readonly string[] RequiredValues =
    [
        "ConnectionStrings:DefaultConnection",
        "Email:FromAddress",
    ];

    /// <summary>Required absolute https URLs on a real host.</summary>
    private static readonly string[] RequiredUrls =
    [
        "Urls:AppBaseUrl",
        "Urls:AdminConsoleBaseUrl",
    ];

    /// <summary>Optional URLs: may be empty, but when set they must be a real https URL.</summary>
    private static readonly string[] OptionalUrls =
    [
        "Urls:WebhookBaseUrl",
        "Urls:CalendarOAuthCallbackBaseUrl",
        "Email:AppBaseUrl",
        "Email:AdminConsoleBaseUrl",
    ];

    /// <summary>Reserved names (RFC 2606 / RFC 6761) and the documentation domains: never a deployed host.</summary>
    private static readonly string[] PlaceholderHostSuffixes =
    [
        ".test", ".example", ".invalid", ".localhost", ".local",
    ];

    private static readonly string[] PlaceholderHosts =
    [
        "localhost", "example.com", "example.org", "example.net",
    ];

    public static void Validate(IConfiguration configuration, string environmentName)
    {
        if (ConfigurationStartupValidator.IsLocalValidationEnvironment(environmentName))
            return;

        var problems = FindProblems(configuration);
        if (problems.Count == 0)
            return;

        throw new InvalidOperationException(
            $"The API cannot start in the '{environmentName}' environment: {problems.Count} setting(s) are missing or invalid."
            + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p))
            + Environment.NewLine + "Set them as environment variables (Section__Key). See docs/deployment/uat-configuration.md.");
    }

    /// <summary>Every problem, one line each, naming the key and never its value.</summary>
    public static IReadOnlyList<string> FindProblems(IConfiguration configuration)
    {
        var problems = new List<string>();

        foreach (var key in RequiredValues)
        {
            if (ConfigurationStartupValidator.IsMissingOrPlaceholder(configuration[key]))
                problems.Add($"{key} is required and is empty or a placeholder.");
        }

        var masterKey = configuration["Encryption:MasterKey"];
        if (ConfigurationStartupValidator.IsMissingOrPlaceholder(masterKey))
            problems.Add("Encryption:MasterKey is required and is empty or a placeholder.");
        else if (masterKey!.Length < MinimumEncryptionMasterKeyLength)
            problems.Add($"Encryption:MasterKey must be at least {MinimumEncryptionMasterKeyLength} characters.");

        var rootDomain = configuration["Tenancy:RootDomain"];
        if (ConfigurationStartupValidator.IsMissingOrPlaceholder(rootDomain))
            problems.Add("Tenancy:RootDomain is required and is empty or a placeholder.");
        else if (rootDomain!.Contains("://", StringComparison.Ordinal) || rootDomain.Contains('/') || rootDomain.Contains(':'))
            problems.Add("Tenancy:RootDomain must be a bare host name (no scheme, port or path).");
        else if (IsPlaceholderHost(rootDomain))
            problems.Add("Tenancy:RootDomain points at a local or example host.");

        foreach (var key in RequiredUrls)
        {
            var value = configuration[key];
            if (ConfigurationStartupValidator.IsMissingOrPlaceholder(value))
                problems.Add($"{key} is required and is empty or a placeholder.");
            else if (UrlProblem(value!) is { } problem)
                problems.Add($"{key} {problem}");
        }

        foreach (var key in OptionalUrls)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (ConfigurationStartupValidator.IsMissingOrPlaceholder(value))
                problems.Add($"{key} is a placeholder. Set a real value or leave it empty.");
            else if (UrlProblem(value) is { } problem)
                problems.Add($"{key} {problem}");
        }

        // CORS: exact-match extra origins. Credentials are allowed for them, so only real https origins.
        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        for (var i = 0; i < origins.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(origins[i]))
                continue;
            if (ConfigurationStartupValidator.IsMissingOrPlaceholder(origins[i]))
                problems.Add($"AllowedOrigins:{i} is a placeholder.");
            else if (UrlProblem(origins[i]) is { } problem)
                problems.Add($"AllowedOrigins:{i} {problem}");
        }

        var adminCookieDomain = configuration["AdminCookieDomain"];
        if (!string.IsNullOrWhiteSpace(adminCookieDomain) && IsPlaceholderHost(adminCookieDomain.TrimStart('.')))
            problems.Add("AdminCookieDomain points at a local or example host. Leave it empty for host-only cookies.");

        // Forwarded headers: a typo here would only surface as a crash on the first request.
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        for (var i = 0; i < proxies.Length; i++)
        {
            if (!IPAddress.TryParse(proxies[i], out _))
                problems.Add($"ForwardedHeaders:KnownProxies:{i} is not an IP address.");
        }
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        for (var i = 0; i < networks.Length; i++)
        {
            if (!IPNetwork.TryParse(networks[i], out _))
                problems.Add($"ForwardedHeaders:KnownNetworks:{i} is not a CIDR network (for example 10.0.0.0/16).");
        }

        // Swagger has no sign-in of its own; it lists every route of both APIs.
        if (configuration.GetValue<bool>("Swagger:Enabled"))
            problems.Add("Swagger:Enabled must be off outside Development: Swagger is not behind admin sign-in.");

        return problems;
    }

    private static string? UrlProblem(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "must be an absolute URL.";
        if (uri.Scheme != Uri.UriSchemeHttps)
            return "must use https.";
        if (IsPlaceholderHost(uri.Host))
            return "points at a local or example host.";
        return null;
    }

    internal static bool IsPlaceholderHost(string host)
    {
        var name = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (name.Length == 0)
            return true;
        if (IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address))
            return true;
        foreach (var placeholder in PlaceholderHosts)
        {
            if (name == placeholder || name.EndsWith("." + placeholder, StringComparison.Ordinal))
                return true;
        }
        return PlaceholderHostSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }
}
