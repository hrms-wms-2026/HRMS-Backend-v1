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
    public void RenderAdminPasswordReset_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "http://localhost:5174"
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "tok-abc" });

        rendered.HtmlBody.Should().Contain("http://localhost:5174/reset-password?token=tok-abc");
        rendered.TextBody.Should().Contain("http://localhost:5174/reset-password?token=tok-abc");
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
}
