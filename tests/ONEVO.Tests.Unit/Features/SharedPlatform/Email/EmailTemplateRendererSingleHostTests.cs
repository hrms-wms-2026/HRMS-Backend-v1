using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Email;

/// <summary>
/// Tenant links in emails: Subdomain mode prefixes the slug onto the host; SingleHost keeps the
/// host and carries the tenant as a ?tenant= query parameter.
/// </summary>
public sealed class EmailTemplateRendererSingleHostTests
{
    private static EmailTemplateRenderer Create(string mode, string appBaseUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tenancy:Mode"] = mode })
            .Build();
        return new EmailTemplateRenderer(Options.Create(new EmailOptions { AppBaseUrl = appBaseUrl }), config);
    }

    [Fact]
    public void PasswordReset_SingleHost_KeepsHostAndMergesTenantIntoQuery()
    {
        var rendered = Create("SingleHost", "https://test.crownus.example")
            .Render("password_reset", new { reset_token = "ab+c/d==", tenant_slug = "acme" });

        var expected = "https://test.crownus.example/auth/reset-password?token=ab%2Bc%2Fd%3D%3D&tenant=acme";
        rendered.TextBody.Should().Contain(expected);
        rendered.HtmlBody.Should().Contain(expected.Replace("&", "&amp;"));
        rendered.TextBody.Should().NotContain("acme.test.crownus.example");
    }

    [Fact]
    public void PasswordReset_Subdomain_PrefixesSlugOntoHost_Unchanged()
    {
        var rendered = Create("Subdomain", "https://test.crownus.example")
            .Render("password_reset", new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("https://acme.test.crownus.example/auth/reset-password?token=tok-123");
        rendered.TextBody.Should().NotContain("tenant=");
    }

    [Fact]
    public void PasswordReset_SingleHost_WithoutSlug_HasNoTenantParam()
    {
        var rendered = Create("SingleHost", "https://test.crownus.example")
            .Render("password_reset", new { reset_token = "tok-123" });

        rendered.TextBody.Should().Contain("https://test.crownus.example/auth/reset-password?token=tok-123");
        rendered.TextBody.Should().NotContain("tenant=");
    }

    [Fact]
    public void EmployeeInvite_SingleHost_KeepsHostAndAddsTenantQuery()
    {
        var rendered = Create("SingleHost", "https://test.crownus.example")
            .Render("employee_onboarding_invite", new
            {
                first_name = "Ada",
                last_name = "Lovelace",
                invite_token = "tok-xyz",
                tenant_slug = "acme"
            });

        rendered.TextBody.Should().Contain("https://test.crownus.example/auth/invitations/tok-xyz?tenant=acme");
        rendered.TextBody.Should().NotContain("acme.test.crownus.example");
    }

    [Fact]
    public void EmployeeInvite_Subdomain_PrefixesSlugOntoHost_Unchanged()
    {
        var rendered = Create("Subdomain", "https://test.crownus.example")
            .Render("employee_onboarding_invite", new
            {
                first_name = "Ada",
                last_name = "Lovelace",
                invite_token = "tok-xyz",
                tenant_slug = "acme"
            });

        rendered.TextBody.Should().Contain("https://acme.test.crownus.example/auth/invitations/tok-xyz");
        rendered.TextBody.Should().NotContain("tenant=");
    }

    [Fact]
    public void PositionApproval_SingleHost_KeepsHostAndAddsTenantQuery()
    {
        var rendered = Create("SingleHost", "https://test.crownus.example")
            .Render("position_change_approval_request", new
            {
                employeeName = "Ada",
                positionName = "CFO",
                changeReason = "promotion",
                tenant_slug = "acme"
            });

        rendered.TextBody.Should().Contain("https://test.crownus.example/people/approvals?tenant=acme");
        rendered.TextBody.Should().NotContain("acme.test.crownus.example");
    }

    [Fact]
    public void PositionApproval_Subdomain_PrefixesSlugOntoHost_Unchanged()
    {
        var rendered = Create("Subdomain", "https://test.crownus.example")
            .Render("position_change_approval_request", new
            {
                employeeName = "Ada",
                positionName = "CFO",
                changeReason = "promotion",
                tenant_slug = "acme"
            });

        rendered.TextBody.Should().Contain("https://acme.test.crownus.example/people/approvals");
        rendered.TextBody.Should().NotContain("tenant=");
    }
}
