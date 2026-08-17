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
            "admin_password_reset" => RenderAdminPasswordReset(fields),
            "admin_password_changed" => RenderAdminPasswordChanged(),
            "platform_manager_invite" => RenderPlatformManagerInvite(fields),
            "employee_onboarding_invite" => RenderEmployeeOnboardingInvite(fields),
            "position_change_approval_request" => RenderPositionChangeApprovalRequest(fields),
            "invoice_email" => RenderInvoiceEmail(fields),
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
            : $"{appBaseUrl.TrimEnd('/')}/auth/invitations/{Uri.EscapeDataString(token)}";

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
        var tenantSlug = Get(f, "tenant_slug");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl)
            ? Get(f, "app_base_url", fallback: string.Empty)
            : _options.AppBaseUrl;
        appBaseUrl = ApplyTenantSlug(appBaseUrl, tenantSlug);
        var resetUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? $"[reset_url placeholder - set Email:AppBaseUrl] token={token}"
            : $"{appBaseUrl.TrimEnd('/')}/auth/reset-password?token={Uri.EscapeDataString(token)}";

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

    private RenderedEmail RenderAdminPasswordReset(IReadOnlyDictionary<string, object?> f)
    {
        var token = Get(f, "reset_token");
        var resetUrl = string.IsNullOrWhiteSpace(_options.AdminConsoleBaseUrl)
            ? $"[reset_url placeholder - set Email:AdminConsoleBaseUrl] token={token}"
            : $"{_options.AdminConsoleBaseUrl.TrimEnd('/')}/auth/reset-password?token={Uri.EscapeDataString(token)}";

        var subject = "Reset your ONEXSO Platform Administration password";
        var html = $"""
            <!doctype html><html><body>
              <p>A password reset was requested for your Platform Administration account.</p>
              <p><a href="{Escape(resetUrl)}">Reset password</a></p>
              <p>If this wasn't you, contact platform support. This link expires in 1 hour.</p>
            </body></html>
            """;
        var text = $"A password reset was requested for your account: {resetUrl}\nIf this wasn't you, contact platform support.";
        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderAdminPasswordChanged()
    {
        const string subject = "Your ONEXSO Platform Administration password was changed";
        const string html = """
            <!doctype html><html><body>
              <p>Your Platform Administration password was changed.</p>
              <p>All existing sessions have been signed out. Your next sign-in will require multi-factor authentication.</p>
              <p>If this wasn't you, contact platform support immediately.</p>
            </body></html>
            """;
        const string text = "Your Platform Administration password was changed. All existing sessions have been signed out. If this wasn't you, contact platform support immediately.";
        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderPlatformManagerInvite(IReadOnlyDictionary<string, object?> f)
    {
        var fullName = Get(f, "full_name");
        var token = Get(f, "invite_token");
        var inviteUrl = string.IsNullOrWhiteSpace(_options.AdminConsoleBaseUrl)
            ? $"[invite_url placeholder - set Email:AdminConsoleBaseUrl] token={token}"
            : $"{_options.AdminConsoleBaseUrl.TrimEnd('/')}/auth/accept-invite?token={Uri.EscapeDataString(token)}";

        var subject = "You've been invited to ONEXSO Platform Administration";
        var html = $"""
            <!doctype html><html><body>
              <p>Hi {Escape(fullName)},</p>
              <p>You've been invited to join ONEXSO Platform Administration.</p>
              <p><a href="{Escape(inviteUrl)}">Accept invitation</a></p>
              <p>This link expires in 72 hours.</p>
            </body></html>
            """;
        var text = $"Hi {fullName},\n\nYou've been invited to join ONEXSO Platform Administration.\nAccept: {inviteUrl}\nThis link expires in 72 hours.";
        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderEmployeeOnboardingInvite(IReadOnlyDictionary<string, object?> f)
    {
        var firstName = Get(f, "first_name");
        var token = Get(f, "invite_token");
        var tenantSlug = Get(f, "tenant_slug");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl)
            ? Get(f, "app_base_url", fallback: string.Empty)
            : _options.AppBaseUrl;
        appBaseUrl = ApplyTenantSlug(appBaseUrl, tenantSlug);
        var inviteUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? $"[invite_url placeholder - set Email:AppBaseUrl] token={token}"
            : $"{appBaseUrl.TrimEnd('/')}/auth/invitations/{Uri.EscapeDataString(token)}";

        var subject = "You're invited to ONEVO — set up your account";

        var html = $"""
            <!doctype html>
            <html>
              <body style="font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; line-height:1.5; color:#0f172a;">
                <h2 style="margin:0 0 12px;">Welcome to ONEVO</h2>
                <p>Hi {Escape(firstName)},</p>
                <p>An account has been set up for you. Click the link below to finish setting up your account and sign in:</p>
                <p><a href="{Escape(inviteUrl)}" style="display:inline-block; padding:10px 16px; background:#0f172a; color:#fff; text-decoration:none; border-radius:6px;">Set up your account</a></p>
                <p>If the button doesn't work, copy this link into your browser:</p>
                <p><code style="word-break:break-all;">{Escape(inviteUrl)}</code></p>
                <hr style="border:none; border-top:1px solid #e2e8f0;" />
                <p style="font-size:12px; color:#64748b;">This link expires in 24 hours. If you didn't expect this email, you can safely ignore it.</p>
              </body>
            </html>
            """;

        var text = $"""
            Hi {firstName},

            An account has been set up for you on ONEVO.

            Set up your account and sign in:
            {inviteUrl}

            This link expires in 24 hours. If you didn't expect this email, you can safely ignore it.
            """;

        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderPositionChangeApprovalRequest(IReadOnlyDictionary<string, object?> f)
    {
        var employeeName = Get(f, "employeeName");
        var positionName = Get(f, "positionName");
        var changeReason = Get(f, "changeReason", fallback: "position change");
        var tenantSlug = Get(f, "tenant_slug");
        var appBaseUrl = string.IsNullOrWhiteSpace(_options.AppBaseUrl) ? string.Empty : _options.AppBaseUrl;
        appBaseUrl = ApplyTenantSlug(appBaseUrl, tenantSlug);
        var approvalsUrl = string.IsNullOrWhiteSpace(appBaseUrl)
            ? "[approvals_url placeholder - set Email:AppBaseUrl]"
            : $"{appBaseUrl.TrimEnd('/')}/people/approvals";

        var subject = $"Approval needed: {Escape(employeeName)}'s {Escape(changeReason)} to {Escape(positionName)}";
        var html = $"""
            <!doctype html><html><body>
              <p>{Escape(employeeName)} has been proposed for a {Escape(changeReason)} into <strong>{Escape(positionName)}</strong>, a sensitive position that requires your approval.</p>
              <p><a href="{Escape(approvalsUrl)}">Review this request</a></p>
            </body></html>
            """;
        var text = $"{employeeName} has been proposed for a {changeReason} into {positionName}, which requires your approval.\nReview: {approvalsUrl}";
        return new RenderedEmail(subject, html, text);
    }

    private RenderedEmail RenderInvoiceEmail(IReadOnlyDictionary<string, object?> f)
    {
        var tenantName = Get(f, "tenant_name");
        var invoiceNumber = Get(f, "invoice_number");
        var status = Get(f, "status");
        var currency = Get(f, "currency");
        var subtotal = Get(f, "subtotal_amount");
        var tax = Get(f, "tax_amount");
        var discount = Get(f, "discount_amount");
        var total = Get(f, "total_amount");
        var periodStart = Get(f, "period_start");
        var periodEnd = Get(f, "period_end");
        var dueAt = Get(f, "due_at");
        var paidAt = Get(f, "paid_at");
        var isReceipt = f.TryGetValue("is_receipt", out var receiptFlag) && receiptFlag is true;

        var subject = isReceipt
            ? $"Payment receipt for invoice {invoiceNumber} - {tenantName}"
            : $"Invoice {invoiceNumber} for {tenantName}";

        var periodLine = string.IsNullOrWhiteSpace(periodStart) || string.IsNullOrWhiteSpace(periodEnd)
            ? string.Empty
            : $"<tr><td style=\"padding:4px 0;\">Billing period</td><td style=\"padding:4px 0;\">{Escape(periodStart)} to {Escape(periodEnd)}</td></tr>";

        var dueLine = !isReceipt && !string.IsNullOrWhiteSpace(dueAt)
            ? $"<tr><td style=\"padding:4px 0;\">Due date</td><td style=\"padding:4px 0;\">{Escape(dueAt)}</td></tr>"
            : string.Empty;

        var paidLine = isReceipt && !string.IsNullOrWhiteSpace(paidAt)
            ? $"<tr><td style=\"padding:4px 0;\">Paid on</td><td style=\"padding:4px 0;\">{Escape(paidAt)}</td></tr>"
            : string.Empty;

        var discountRow = decimal.TryParse(discount, out var discountAmount) && discountAmount > 0
            ? $"<tr><td style=\"padding:4px 8px;\">Discount</td><td style=\"padding:4px 8px; text-align:right;\">-{Escape(currency)} {Escape(discount)}</td></tr>"
            : string.Empty;

        var html = $"""
            <!doctype html>
            <html>
              <body style="font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; line-height:1.5; color:#0f172a;">
                <h2 style="margin:0 0 12px;">{(isReceipt ? "Payment receipt" : "Invoice")}</h2>
                <p>Hello,</p>
                <p>This email is for <strong>{Escape(tenantName)}</strong>.</p>
                <table style="border-collapse:collapse; margin:16px 0;">
                  <tr><td style="padding:4px 0;">Invoice number</td><td style="padding:4px 0;"><strong>{Escape(invoiceNumber)}</strong></td></tr>
                  <tr><td style="padding:4px 0;">Status</td><td style="padding:4px 0;">{Escape(status)}</td></tr>
                  {periodLine}
                  {dueLine}
                  {paidLine}
                </table>
                <table style="border-collapse:collapse; width:100%; max-width:480px; border-top:1px solid #e2e8f0;">
                  <tr><td style="padding:4px 8px;">Subtotal</td><td style="padding:4px 8px; text-align:right;">{Escape(currency)} {Escape(subtotal)}</td></tr>
                  <tr><td style="padding:4px 8px;">Tax</td><td style="padding:4px 8px; text-align:right;">{Escape(currency)} {Escape(tax)}</td></tr>
                  {discountRow}
                  <tr><td style="padding:8px 8px; font-weight:600;">Total</td><td style="padding:8px 8px; text-align:right; font-weight:600;">{Escape(currency)} {Escape(total)}</td></tr>
                </table>
                <p style="font-size:12px; color:#64748b;">If you have questions about this invoice, contact your platform administrator.</p>
              </body>
            </html>
            """;

        var text = $"""
            {(isReceipt ? "Payment receipt" : "Invoice")} for {tenantName}
            Invoice number: {invoiceNumber}
            Status: {status}
            Total: {currency} {total}
            """;

        return new RenderedEmail(subject, html, text);
    }

    /// <summary>
    /// Base-domain forgot-password can match users in multiple tenants; each reset link must land
    /// the browser on that tenant's host so HostTenantResolutionMiddleware resolves the same tenant
    /// the token was issued for. Reuses the app's own host shape (subdomain-of-root, same
    /// scheme/port as configured) rather than inventing a second convention.
    /// </summary>
    private static string ApplyTenantSlug(string appBaseUrl, string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(appBaseUrl) || string.IsNullOrWhiteSpace(tenantSlug))
            return appBaseUrl;

        if (!Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var parsed))
            return appBaseUrl;

        var builder = new UriBuilder(parsed) { Host = $"{tenantSlug}.{parsed.Host}" };
        return builder.Uri.ToString();
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
