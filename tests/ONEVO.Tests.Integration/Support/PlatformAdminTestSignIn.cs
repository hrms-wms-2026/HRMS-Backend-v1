using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ONEVO.Tests.Integration.Support;

/// <summary>An admin_session cookie pair ready to send: <c>SessionCookie</c> is "admin_session=...".</summary>
public sealed record AdminTestSession(string SessionCookie, string CsrfToken);

/// <summary>
/// Signs a platform admin in the way the admin console does, including mandatory MFA (F-02).
/// There is no test-only bypass of the MFA gate: a not-yet-enrolled admin completes the real
/// mfa/enable + mfa/confirm-setup flow with a computed TOTP code, which lifts the restriction
/// on the same session; an already-enrolled admin answers the 202 challenge via mfa/verify.
/// </summary>
public static class PlatformAdminTestSignIn
{
    // Secrets enrolled earlier in this test process, keyed by email. Each test class normally
    // gets a fresh database, so this is only consulted when a database is reused (CROWNUS_TEST_DB).
    private static readonly ConcurrentDictionary<string, string> EnrolledSecrets = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<AdminTestSession> SignInAsync(
        HttpClient client, string email, string password, string? host = null)
    {
        using var login = await SendAsync(client, HttpMethod.Post, "/admin/v1/auth/login", host,
            new { email, password });

        if (login.StatusCode == HttpStatusCode.Accepted)
            return await AnswerMfaChallengeAsync(client, login, email, host);

        await EnsureSuccessAsync(login, "admin login");
        var session = ReadSession(login);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("mfa_enrollment_required", out var required) && required.GetBoolean())
            await CompleteEnrollmentAsync(client, session, email, host);

        return session;
    }

    /// <summary>Runs mfa/enable then mfa/confirm-setup on an enrollment-only session and returns the TOTP secret.</summary>
    public static async Task<string> CompleteEnrollmentAsync(
        HttpClient client, AdminTestSession session, string email, string? host = null)
    {
        using var enable = await SendAsync(client, HttpMethod.Post, "/admin/v1/auth/mfa/enable", host,
            new { }, session.SessionCookie, session.CsrfToken);
        await EnsureSuccessAsync(enable, "mfa/enable");
        var secret = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        using var confirm = await SendAsync(client, HttpMethod.Post, "/admin/v1/auth/mfa/confirm-setup", host,
            new { code = TestTotp.CurrentCode(secret) }, session.SessionCookie, session.CsrfToken);
        await EnsureSuccessAsync(confirm, "mfa/confirm-setup");

        EnrolledSecrets[email] = secret;
        return secret;
    }

    private static async Task<AdminTestSession> AnswerMfaChallengeAsync(
        HttpClient client, HttpResponseMessage login, string email, string? host)
    {
        if (!EnrolledSecrets.TryGetValue(email, out var secret))
            throw new InvalidOperationException(
                $"Admin '{email}' is already MFA-enrolled in this database, but its TOTP secret is unknown to this " +
                "test run. Use a fresh test database.");

        var challenge = ReadCookies(login)["admin_mfa"];
        using var verify = await SendAsync(client, HttpMethod.Post, "/admin/v1/auth/mfa/verify", host,
            new { code = TestTotp.CurrentCode(secret) }, cookie: $"admin_mfa={challenge}");
        await EnsureSuccessAsync(verify, "mfa/verify");
        return ReadSession(verify);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string? host, object body,
        string? cookie = null, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (host is not null)
            request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        return await client.SendAsync(request);
    }

    private static AdminTestSession ReadSession(HttpResponseMessage response)
    {
        var cookies = ReadCookies(response);
        return new AdminTestSession($"admin_session={cookies["admin_session"]}", cookies["admin_csrf"]);
    }

    private static Dictionary<string, string> ReadCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return cookies;

        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var index = pair.IndexOf('=');
            if (index > 0)
                cookies[pair[..index].Trim()] = pair[(index + 1)..].Trim();
        }

        return cookies;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"{step} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}

/// <summary>RFC 6238 TOTP (SHA-1, 30-second step, 6 digits), matching what an authenticator app computes.</summary>
public static class TestTotp
{
    public static string CurrentCode(string base32Secret) =>
        Compute(base32Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

    public static string Compute(string base32Secret, long timeStep)
    {
        var counter = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        using var hmac = new HMACSHA1(Base32Decode(base32Secret));
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in cleaned)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Invalid base32 character '{c}'.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
