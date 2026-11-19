using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Helpers;

public sealed class RichTextRulesTests
{
    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<div onclick=\"steal()\">hi</div>")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"java&#115;cript:alert(1)\">x</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>")]
    [InlineData("<form action=\"https://evil.example\"><input></form>")]
    public void HasActiveContent_ActiveMarkup_ReturnsTrue(string html)
    {
        Assert.True(RichTextRules.HasActiveContent(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("<div><b>Bold</b> and <a href=\"https://example.com\">a link</a></div>")]
    [InlineData("<div><span contenteditable=\"false\" data-employee-id=\"e-1\">@Ana</span></div>")]
    [InlineData("<div><input type=\"checkbox\" checked> todo</div>")]
    [InlineData("<img src=\"data:image/png;base64,iVBORw0KGgo=\">")]
    [InlineData("<span style=\"color: rgb(255, 0, 0);\">Online meeting</span>")]
    public void HasActiveContent_EditorMarkup_ReturnsFalse(string? html)
    {
        Assert.False(RichTextRules.HasActiveContent(html));
    }
}
