using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class InvoiceStatusRulesTests
{
    [Theory]
    [InlineData("draft", true)]
    [InlineData("open", true)]
    [InlineData("paid", false)]
    [InlineData("void", false)]
    public void CanMarkPaid_AllowsDraftAndOpenOnly(string status, bool expected) =>
        Assert.Equal(expected, InvoiceStatusRules.CanMarkPaid(status));

    [Theory]
    [InlineData("draft", true)]
    [InlineData("open", true)]
    [InlineData("paid", false)]
    [InlineData("void", false)]
    public void CanVoid_AllowsDraftAndOpenOnly(string status, bool expected) =>
        Assert.Equal(expected, InvoiceStatusRules.CanVoid(status));

    [Theory]
    [InlineData("draft")]
    [InlineData("open")]
    [InlineData("paid")]
    [InlineData("void")]
    public void IsValid_AcceptsKnownStatuses(string status) =>
        Assert.True(InvoiceStatusRules.IsValid(status));

    [Fact]
    public void IsValid_RejectsUnknownStatus() =>
        Assert.False(InvoiceStatusRules.IsValid("cancelled"));
}
