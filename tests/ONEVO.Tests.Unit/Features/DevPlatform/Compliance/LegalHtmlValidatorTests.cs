using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class LegalHtmlValidatorTests
{
    [Theory]
    [InlineData("<h1>Title</h1><p>Body <strong>text</strong> and <em>more</em>.</p>")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>")]
    [InlineData("<blockquote>Quoted</blockquote>")]
    [InlineData("<p>Line one<br>Line two</p>")]
    [InlineData("<p>Line one<br/>Line two</p>")]
    [InlineData("<p>Line one<br />Line two</p>")]
    [InlineData("<a href=\"https://example.com\">link</a>")]
    [InlineData("<a href=\"mailto:test@example.com\">mail</a>")]
    [InlineData("<a href=\"/relative/path\">rel</a>")]
    [InlineData("<a href=\"https://example.com\" title=\"Example\">link</a>")]
    [InlineData("<table><thead><tr><th colspan=\"2\">H</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>")]
    [InlineData("<td rowspan=\"3\" colspan=\"2\">Cell</td>")]
    public void Validate_AllowsSafeFormatting(string html)
    {
        var result = LegalHtmlValidator.Validate(html);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p onclick=\"alert(1)\">click</p>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">")]
    [InlineData("<p onload=\"alert(1)\">x</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<object data=\"x\"></object>")]
    [InlineData("<embed src=\"x\">")]
    [InlineData("<style>body{background:url(x)}</style>")]
    [InlineData("<svg onload=\"alert(1)\"></svg>")]
    [InlineData("<div class=\"x\">unlisted tag</div>")]
    [InlineData("<p style=\"color:red\">unlisted attribute</p>")]
    // Unquoted/malformed attribute regression coverage - these must never slip past the
    // allowlist just because the value wasn't wrapped in quotes.
    [InlineData("<p onclick=alert(1)>x</p>")]
    [InlineData("<a href=javascript:alert(1)>x</a>")]
    [InlineData("<a href=https://example.com>x</a>")]
    [InlineData("<td colspan=2>x</td>")]
    [InlineData("<p class=x>x</p>")]
    [InlineData("<a href=\"https://example.com\" title=unquoted>x</a>")]
    [InlineData("<td colspan=\"2\" rowspan=2>x</td>")]
    // Protocol-relative hrefs inherit whatever scheme the page loaded under and can point
    // off-origin just like javascript:/data: - a leading "/" is only safe when it isn't "//".
    [InlineData("<a href=\"//evil.example/path\">x</a>")]
    [InlineData("<a href=\"//evil.example\">x</a>")]
    public void Validate_RejectsUnsafeOrUnlistedContent(string html)
    {
        var result = LegalHtmlValidator.Validate(html);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_RejectsEmptyContent()
    {
        var result = LegalHtmlValidator.Validate("   ");

        result.IsValid.Should().BeFalse();
    }
}
