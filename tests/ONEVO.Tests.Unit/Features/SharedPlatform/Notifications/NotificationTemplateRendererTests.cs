using FluentAssertions;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationTemplateRendererTests
{
    private readonly NotificationTemplateRenderer _renderer = new();

    private static readonly NotificationEventDefinition Event = new(
        "test_event", "work_management", "Test event",
        SupportedChannels: NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Tray,
        DefaultChannels: NotificationChannels.InApp,
        RequiredChannels: NotificationChannels.None,
        AllowedVariables: [new("actorName", Required: true), new("targetTitle"), new("link")],
        AllowedRecipientRules: [NotificationRecipientRules.Default],
        AllowedActionKeys: [NotificationActionKeys.OpenRelated]);

    private static NotificationTemplateContent Content(
        string inAppTitle = "Approval needed", string inAppBody = "{{actorName}} asked about \"{{targetTitle}}\"",
        string? subject = null, string? html = null, string? css = null, string? text = null,
        string? trayTitle = null, string? trayBody = null, string actionKey = NotificationActionKeys.OpenRelated)
        => new(inAppTitle, inAppBody, subject, html, css, text, trayTitle, trayBody, actionKey);

    private static Dictionary<string, string> Vars(string actor = "Priya", string target = "Q4 plan") =>
        new() { ["actorName"] = actor, ["targetTitle"] = target };

    [Fact]
    public void Render_SubstitutesVariablesExactly()
    {
        var result = _renderer.Render(Content(), Event, Vars());

        result.IsSuccess.Should().BeTrue();
        result.Content!.InAppBody.Should().Be("Priya asked about \"Q4 plan\"");
    }

    [Fact]
    public void Render_MissingRequiredVariable_Fails()
    {
        var result = _renderer.Render(Content(), Event, new Dictionary<string, string> { ["targetTitle"] = "x" });

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("actorName"));
    }

    [Fact]
    public void Render_OptionalVariableMissing_RendersEmpty()
    {
        var result = _renderer.Render(Content(), Event, new Dictionary<string, string> { ["actorName"] = "Priya" });

        result.Content!.InAppBody.Should().Be("Priya asked about \"\"");
    }

    [Fact]
    public void Validate_UnknownPlaceholder_IsAnError()
    {
        _renderer.Validate(Content(inAppBody: "Hi {{salary}}"), Event).Should().ContainSingle(e => e.Contains("salary"));
    }

    [Fact]
    public void Validate_UnknownActionKey_IsAnError()
    {
        _renderer.Validate(Content(actionKey: "run_script"), Event).Should().ContainSingle(e => e.Contains("run_script"));
    }

    [Fact]
    public void Render_EscapesVariableValuesInEmailHtml()
    {
        var result = _renderer.Render(Content(html: "<p>{{actorName}}</p>"), Event, Vars(actor: "<script>alert(1)</script>"));

        result.Content!.EmailHtml.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void Render_StripsLineBreaksFromSubject()
    {
        var result = _renderer.Render(Content(subject: "Re: {{targetTitle}}"), Event, Vars(target: "a\r\nBcc: x@evil.test"));

        result.Content!.EmailSubject.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void Render_InlinesSupportedCss()
    {
        var result = _renderer.Render(
            Content(html: "<p class=\"note\" style=\"margin: 0\">Hi</p>", css: "p { color: red; } .note { font-weight: bold; }"),
            Event, Vars());

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Content!.EmailHtml.Should().Contain("color: red").And.Contain("font-weight: bold").And.Contain("margin: 0");
        result.Content.EmailHtml.IndexOf("font-weight", StringComparison.Ordinal)
            .Should().BeLessThan(result.Content.EmailHtml.IndexOf("margin", StringComparison.Ordinal), "existing inline style wins");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p onclick=\"steal()\">x</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<style>p{color:red}</style>")]
    [InlineData("<iframe src=\"https://example.test\"></iframe>")]
    [InlineData("<p style=\"background:url(https://track.test/p.gif)\">x</p>")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://x.test/a.css\">")]
    public void Validate_RejectsActiveOrUnsafeHtml(string html)
    {
        _renderer.Validate(Content(html: html), Event).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("@import url(https://x.test/a.css);")]
    [InlineData("div > p { color: red; }")]
    [InlineData("p { background: url(https://x.test/a.png); }")]
    [InlineData("p { width: expression(alert(1)); }")]
    public void Validate_RejectsUnsupportedCss(string css)
    {
        _renderer.Validate(Content(html: "<p>x</p>", css: css), Event).Should().NotBeEmpty();
    }

    [Fact]
    public void Render_NeutralisesUnsafeUrlFromVariable()
    {
        var result = _renderer.Render(Content(html: "<a href=\"{{link}}\">Open</a>"), Event,
            new Dictionary<string, string> { ["actorName"] = "P", ["link"] = "javascript:alert(1)" });

        result.Content!.EmailHtml.Should().NotContain("javascript:").And.Contain("href=\"#\"");
    }

    [Fact]
    public void Render_KeepsSafeUrlFromVariable()
    {
        var result = _renderer.Render(Content(html: "<a href=\"{{link}}\">Open</a>"), Event,
            new Dictionary<string, string> { ["actorName"] = "P", ["link"] = "https://app.onevo.test/inbox?a=1&b=2" });

        result.Content!.EmailHtml.Should().Contain("href=\"https://app.onevo.test/inbox?a=1&amp;b=2\"");
    }

    [Fact]
    public void Render_FallsBackToInAppCopyForEmptyChannels()
    {
        var result = _renderer.Render(Content(), Event, Vars(actor: "A & B"));

        var c = result.Content!;
        c.EmailSubject.Should().Be("Approval needed");
        c.TrayTitle.Should().Be("Approval needed");
        c.TrayBody.Should().Be(c.InAppBody);
        c.EmailText.Should().Be(c.InAppBody);
        c.EmailHtml.Should().Contain("A &amp; B");
    }
}
