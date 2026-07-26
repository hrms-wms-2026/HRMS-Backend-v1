using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ONEVO.Infrastructure.ExternalServices.Email;

public interface IEmailTemplateRenderer
{
    RenderedEmail Render(string templateId, object data);
}

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Tiny inline template registry. Keeps the dependency surface zero. As the
/// catalog grows we can swap this for a real templating library (Razor, Liquid,
/// MJML) without touching call sites.
/// </summary>
public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly EmailOptions _options;

    public EmailTemplateRenderer(IOptions<EmailOptions> options) => _options = options.Value;

    public RenderedEmail Render(string templateId, object data)
    {
        var fields = ToDictionary(data);
        return templateId switch
        {
            "tenant_owner_invite" => RenderTenantOwnerInvite(fields),
            "password_reset" => RenderPasswordReset(fields),
            _ => throw new InvalidOperationException(
                $"Unknown email template '{templateId}'. Add a case in EmailTemplateRenderer.")
        };
    }

    private RenderedEmail RenderTenantOwnerInvite(IReadOnlyDictionary<string, object?> f)
    {
        var company = Get(f, "tenant_company");
        var firstName = Get(f, "invited_first_name");
        var token = Get(f, "invite_token");
        var expires = Get(f, "invite_expires_at");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl)
            ? Get(f, "app_base_url", fallback: string.Empty)
            : _options.AppBaseUrl;
        var inviteUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? $"[invite_url placeholder - set Email:AppBaseUrl] token={token}"
            : $"{appBaseUrl.TrimEnd('/')}/auth/invitations/{token}";

        var subject = $"You're invited to manage {company} on ONEVO";

        var html = $"""
            <!doctype html>
            <html>
              <body style="font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; line-height:1.5; color:#0f172a;">
                <h2 style="margin:0 0 12px;">Welcome to ONEVO</h2>
                <p>Hi {Escape(firstName)},</p>
                <p>You've been invited to manage <strong>{Escape(company)}</strong> on the ONEVO platform.</p>
                <p>To finish setting up your account, click the link below before <em>{Escape(expires)}</em>:</p>
                <p><a href="{Escape(inviteUrl)}" style="display:inline-block; padding:10px 16px; background:#0f172a; color:#fff; text-decoration:none; border-radius:6px;">Accept your invitation</a></p>
                <p>If the button doesn't work, copy this link into your browser:</p>
                <p><code style="word-break:break-all;">{Escape(inviteUrl)}</code></p>
                <hr style="border:none; border-top:1px solid #e2e8f0;" />
                <p style="font-size:12px; color:#64748b;">If you didn't expect this email, you can safely ignore it. The invitation will expire automatically.</p>
              </body>
            </html>
            """;

        var text = $"""
            Hi {firstName},

            You've been invited to manage {company} on ONEVO.

            Accept your invitation before {expires}:
            {inviteUrl}

            If you didn't expect this email, you can safely ignore it.
            """;

        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderPasswordReset(IReadOnlyDictionary<string, object?> f)
    {
        var token = Get(f, "reset_token");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl)
            ? Get(f, "app_base_url", fallback: string.Empty)
            : _options.AppBaseUrl;
        var resetUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? $"[reset_url placeholder - set Email:AppBaseUrl] token={token}"
            : $"{appBaseUrl.TrimEnd('/')}/auth/reset-password?token={token}";

        var subject = "Reset your ONEVO password";
        var html = $"""
            <!doctype html><html><body>
              <p>We received a request to reset your ONEVO password.</p>
              <p><a href="{Escape(resetUrl)}">Reset password</a></p>
              <p>If you didn't request this, you can ignore this email.</p>
            </body></html>
            """;
        var text = $"Reset your ONEVO password: {resetUrl}";
        return new RenderedEmail(subject, html, text);
    }

    private static string Get(
        IReadOnlyDictionary<string, object?> fields,
        string key,
        string fallback = "")
    {
        if (!fields.TryGetValue(key, out var raw) || raw is null)
            return fallback;
        return raw switch
        {
            DateTimeOffset dto => dto.ToString("u", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString() ?? fallback
        };
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    private static IReadOnlyDictionary<string, object?> ToDictionary(object data)
    {
        if (data is IReadOnlyDictionary<string, object?> roDict)
            return roDict;
        if (data is IDictionary<string, object?> dict)
            return new Dictionary<string, object?>(dict);

        // Fallback: round-trip through JSON to flatten anonymous types.
        var json = JsonSerializer.Serialize(data);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }
        return result;
    }
}
