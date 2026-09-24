using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class InvoiceStatusRulesResendTests
{
    [Theory]
    [InlineData("open", true)]
    [InlineData("paid", true)]
    [InlineData("draft", false)]
    [InlineData("void", false)]
    public void CanResendEmail_AllowsOpenAndPaidOnly(string status, bool expected) =>
        Assert.Equal(expected, InvoiceStatusRules.CanResendEmail(status));
}
