using FluentAssertions;
using Microsoft.Extensions.Options;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Email;

public sealed class EmailTemplateRendererTests
{
    [Fact]
    public void RenderPasswordReset_WithoutTenantSlug_UsesFlatAppBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("password_reset", new { reset_token = "tok-123" });

        rendered.HtmlBody.Should().Contain("http://localhost:5173/auth/reset-password?token=tok-123");
        rendered.TextBody.Should().Contain("http://localhost:5173/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithTenantSlug_PrefixesSlugOntoAppBaseUrlHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("http://acme.localhost:5173/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithTenantSlug_PreservesSchemeAndPortOnProductionStyleHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "https://onevo.io"
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("https://acme.onevo.io/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithEmptyAppBaseUrl_FallsBackToPlaceholderRegardlessOfTenantSlug()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = string.Empty
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("token=tok-123");
        rendered.TextBody.Should().Contain("placeholder");
    }

    [Fact]
    public void RenderPasswordReset_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("password_reset", new { reset_token = "ab+c/d==" });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderAdminPasswordReset_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "http://localhost:5174"
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "tok-abc" });

        rendered.HtmlBody.Should().Contain("http://localhost:5174/auth/reset-password?token=tok-abc");
        rendered.TextBody.Should().Contain("http://localhost:5174/auth/reset-password?token=tok-abc");
    }

    [Fact]
    public void RenderAdminPasswordReset_WithEmptyAdminConsoleBaseUrl_FallsBackToPlaceholder()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = string.Empty
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "tok-abc" });

        rendered.TextBody.Should().Contain("token=tok-abc");
        rendered.TextBody.Should().Contain("placeholder");
    }

    [Fact]
    public void RenderAdminPasswordReset_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "http://localhost:5174"
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "ab+c/d==" });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderPlatformManagerInvite_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "https://admin.localhost:4200"
        }));

        var rendered = renderer.Render("platform_manager_invite", new
        {
            full_name = "New Manager",
            invite_token = "tok-xyz"
        });

        rendered.HtmlBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
        rendered.TextBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
    }

    [Fact]
    public void RenderPlatformManagerInvite_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "https://admin.localhost:4200"
        }));

        var rendered = renderer.Render("platform_manager_invite", new
        {
            full_name = "New Manager",
            invite_token = "ab+c/d=="
        });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderAdminPasswordChanged_ProducesSecurityNoticeCopy()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("admin_password_changed", new { });

        rendered.HtmlBody.Should().Contain("password was changed");
        rendered.TextBody.Should().Contain("If this wasn't you", "must tell the recipient how to react if this wasn't them");
    }

    [Fact]
    public void RenderInvoiceEmail_IncludesInvoiceNumberTenantNameAmountAndDueDate()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("invoice_email", new
        {
            tenant_name = "Acme Co",
            invoice_number = "INV-1001",
            status = "open",
            currency = "USD",
            subtotal_amount = 100m,
            tax_amount = 10m,
            discount_amount = 0m,
            total_amount = 110m,
            period_start = "2026-08-01",
            period_end = "2026-08-31",
            due_at = "2026-08-20 00:00:00Z",
            paid_at = (string?)null,
            is_receipt = false
        });

        rendered.Subject.Should().Contain("INV-1001");
        rendered.Subject.Should().Contain("Acme Co");
        rendered.HtmlBody.Should().Contain("INV-1001");
        rendered.HtmlBody.Should().Contain("Acme Co");
        rendered.HtmlBody.Should().Contain("USD");
        rendered.HtmlBody.Should().Contain("110");
        rendered.HtmlBody.Should().Contain("2026-08-20 00:00:00Z");
    }

    [Fact]
    public void RenderInvoiceEmail_PaidStatus_UsesReceiptSubject()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("invoice_email", new
        {
            tenant_name = "Acme Co",
            invoice_number = "INV-2002",
            status = "paid",
            currency = "USD",
            subtotal_amount = 50m,
            tax_amount = 0m,
            discount_amount = 0m,
            total_amount = 50m,
            period_start = "2026-08-01",
            period_end = "2026-08-31",
            due_at = (string?)null,
            paid_at = "2026-08-13 12:00:00Z",
            is_receipt = true
        });

        rendered.Subject.Should().Contain("Payment receipt");
        rendered.HtmlBody.Should().Contain("Paid on");
    }
}
