using FluentAssertions;
using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common;

public class EntityIconRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lucide:rocket")]
    [InlineData("lucide:calendar-check-2")]
    [InlineData("emoji:🚀")]
    [InlineData("emoji:👨‍👩‍👧‍👦")]
    [InlineData("emoji:👍🏽")]
    [InlineData("emoji:🇱🇰")]
    [InlineData("emoji:1️⃣")]
    [InlineData("emoji:ℹ️")]
    public void Accepts_no_icon_a_lucide_name_or_one_emoji(string? icon)
        => EntityIconRules.IsValid(icon).Should().BeTrue();

    [Theory]
    [InlineData("rocket")]
    [InlineData("lucide:")]
    [InlineData("lucide:Rocket")]
    [InlineData("lucide:rocket--x")]
    [InlineData("lucide:<svg>")]
    [InlineData("emoji:")]
    [InlineData("emoji:🚀🚀")]
    [InlineData("emoji:hi")]
    [InlineData("emoji:a")]
    [InlineData("emoji:1")]
    [InlineData("fa:rocket")]
    public void Rejects_anything_else(string icon)
        => EntityIconRules.IsValid(icon).Should().BeFalse();

    [Theory]
    [InlineData(null, true)]
    [InlineData("#7C5CFF", true)]
    [InlineData("#7c5cff", true)]
    [InlineData("7C5CFF", false)]
    [InlineData("red", false)]
    public void Colours_are_hex(string? color, bool valid)
        => EntityIconRules.IsValidColor(color).Should().Be(valid);
}
