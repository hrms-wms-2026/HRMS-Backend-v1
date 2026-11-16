using FluentAssertions;
using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Helpers;

public sealed class BasicRichHtmlTests
{
    [Fact]
    public void AllowedTags_are_exactly_the_shared_list()
    {
        BasicRichHtml.AllowedTags.Should().BeEquivalentTo(
        [
            "p", "br", "strong", "b", "em", "i", "u", "s", "h1", "h2", "h3", "h4", "ul", "ol", "li",
            "blockquote", "a", "code", "pre", "table", "thead", "tbody", "tr", "th", "td",
        ]);
    }

    [Fact]
    public void AllowedAttributes_are_exactly_the_shared_list()
    {
        BasicRichHtml.AllowedAttributes.Keys.Should().BeEquivalentTo(["a", "td", "th"]);
        BasicRichHtml.AllowedAttributes["a"].Should().BeEquivalentTo(["href", "title"]);
        BasicRichHtml.AllowedAttributes["td"].Should().BeEquivalentTo(["colspan", "rowspan"]);
        BasicRichHtml.AllowedAttributes["th"].Should().BeEquivalentTo(["colspan", "rowspan"]);
    }

    [Theory]
    [InlineData("<p>Hi <strong>there</strong></p>")]
    [InlineData("<ul><li>one</li><li>two</li></ul>")]
    [InlineData("<pre><code>var x = 1;</code></pre>")]
    [InlineData("<p><a href=\"https://crownus.example/help\" title=\"Help\">help</a></p>")]
    public void Validate_accepts_allowlisted_markup(string html)
    {
        BasicRichHtml.Validate(html).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p><img src=\"x\"></p>")]
    [InlineData("<p><span style=\"color:red\">x</span></p>")]
    [InlineData("<p onclick=\"x()\">x</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html,x\">x</a>")]
    public void Validate_rejects_anything_outside_the_allowlist(string html)
    {
        var check = BasicRichHtml.Validate(html);

        check.IsValid.Should().BeFalse();
        check.Error.Should().NotBeNullOrWhiteSpace();
    }

    // Review finding I2: tags the tag scanner cannot match, and entity- or whitespace-obfuscated schemes.
    [Theory]
    [InlineData("<img/src=x onerror=alert(1)>")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\"<p>x</p>")]
    [InlineData("<p>x</p><!--<script>-->")]
    [InlineData("<a href=\"javascript&colon;alert(1)\">x</a>")]
    [InlineData("<a href=\"javascript&#58;alert(1)\">x</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">x</a>")]
    [InlineData("<a href=\"\\\\evil.example\">x</a>")]
    [InlineData("<a href=\"/\\evil.example\">x</a>")]
    [InlineData("<a href=\"https:evil.example\">x</a>")]
    [InlineData("<a href=\"\">x</a>")]
    public void Validate_rejects_markup_the_scanner_cannot_prove_safe(string html)
    {
        BasicRichHtml.Validate(html).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("<a href=\"https://x.example/a?b=1\">x</a>")]
    [InlineData("<a href=\"mailto:help@x.example\">x</a>")]
    [InlineData("<a href=\"/relative/path\">x</a>")]
    [InlineData("<a href=\"#section-2\">x</a>")]
    [InlineData("<p>5 &lt; 6 and 7 > 3</p>")]
    public void Validate_accepts_safe_links_and_text(string html)
    {
        BasicRichHtml.Validate(html).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_accepts_the_editors_cleaned_output_of_a_word_paste()
    {
        const string cleaned =
            "<h2>Steps</h2><p>Open <strong>Settings</strong> and <em>save</em>.</p>" +
            "<ol><li>First</li><li>Second</li></ol><blockquote>Note</blockquote>";

        BasicRichHtml.Validate(cleaned).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("error at line 5 < 6", false)]
    [InlineData("a <b and c> d", false)]
    [InlineData("plain text", false)]
    [InlineData("<p>x</p>", true)]
    [InlineData("first</li>", true)]
    [InlineData("<br/>", true)]
    public void IsRichHtml_needs_a_recognisable_allowlisted_tag(string value, bool expected)
    {
        BasicRichHtml.IsRichHtml(value).Should().Be(expected);
    }

    [Fact]
    public void VisibleLength_counts_text_not_markup()
    {
        BasicRichHtml.VisibleLength("<p><strong>abc</strong></p>").Should().Be(3);
        BasicRichHtml.VisibleLength("<p>a &amp; b</p>").Should().Be(5);
        BasicRichHtml.VisibleLength("plain < text").Should().Be(12);
    }

    [Fact]
    public void ToPlainText_turns_lists_blocks_and_links_into_text()
    {
        BasicRichHtml.ToPlainText("<ul><li>a</li><li>b</li></ul>").Should().Be("- a\n- b");
        BasicRichHtml.ToPlainText("<p>One</p><p>Two &amp; three</p>").Should().Be("One\nTwo & three");
        BasicRichHtml.ToPlainText("<p>See <a href=\"https://x.example\">docs</a></p>").Should().Be("See docs (https://x.example)");
        BasicRichHtml.ToPlainText("plain\ntext").Should().Be("plain\ntext");
    }
}
