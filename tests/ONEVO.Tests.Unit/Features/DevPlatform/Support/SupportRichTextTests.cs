using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

/// <summary>Support descriptions and comments accept the shared basic rich text (DEC-035).</summary>
public sealed class SupportRichTextTests
{
    [Fact]
    public void Ticket_with_rich_description_is_valid()
    {
        SupportTicketRules.ValidateTicket("Export fails", "<p>Steps:</p><ol><li>Open</li><li>Export</li></ol>", "high", null)
            .Should().BeNull();
    }

    [Fact]
    public void Ticket_with_disallowed_markup_is_rejected_with_the_allowlist_message()
    {
        SupportTicketRules.ValidateTicket("Export fails", "<p>Hi <span style=\"color:red\">x</span></p>", "high", null)
            .Should().Contain("Disallowed tag '<span>'");
    }

    [Fact]
    public void Description_limit_counts_visible_text_not_markup()
    {
        var atLimit = "<p><strong>" + new string('a', 10000) + "</strong></p>";
        var overLimit = "<p><strong>" + new string('a', 10001) + "</strong></p>";

        SupportTicketRules.ValidateTicket("S", atLimit, "low", null).Should().BeNull();
        SupportTicketRules.ValidateTicket("S", overLimit, "low", null).Should().Contain("10000");
    }

    [Fact]
    public void Raw_markup_is_capped_even_when_the_visible_text_is_short()
    {
        var bloated = string.Concat(Enumerable.Repeat("<p><strong><em>a</em></strong></p>", 1700));

        SupportTicketRules.ValidateComment(bloated).Should().Contain("50000");
    }

    [Fact]
    public void Plain_text_tickets_keep_todays_rules()
    {
        SupportTicketRules.ValidateTicket("S", "error at line 5 < 6", "low", null).Should().BeNull();
        SupportTicketRules.ValidateTicket("S", new string('a', 10001), "low", null).Should().Contain("10000");
    }

    [Fact]
    public void Rich_comment_is_valid_and_obfuscated_link_is_rejected()
    {
        SupportTicketRules.ValidateComment("<p>See <a href=\"https://x.example\">docs</a></p>").Should().BeNull();
        SupportTicketRules.ValidateComment("<p><a href=\"javascript&colon;x\">x</a></p>").Should().NotBeNull();
    }

    [Fact]
    public void Email_excerpt_is_plain_text()
    {
        SupportTicketRules.EmailExcerpt("<ul><li>a</li><li>b</li></ul>").Should().Be("- a\n- b");
        SupportTicketRules.EmailExcerpt("plain\ntext").Should().Be("plain\ntext");
    }
}
