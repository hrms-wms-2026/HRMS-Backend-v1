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
}
