using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Helpers;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class PaymentGatewayKeyRulesTests
{
    [Theory]
    [InlineData("stripe_sandbox")]
    [InlineData("stripe_live")]
    [InlineData("stripe-live")]
    [InlineData("a1")]
    public void IsValid_AcceptsLowercaseUrlSafeKeys(string gatewayKey)
    {
        Assert.True(PaymentGatewayKeyRules.IsValid(gatewayKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Stripe_live")]
    [InlineData("stripe live")]
    [InlineData("stripe.live")]
    [InlineData("../stripe_live")]
    [InlineData("1stripe")]
    public void IsValid_RejectsMalformedKeys(string gatewayKey)
    {
        Assert.False(PaymentGatewayKeyRules.IsValid(gatewayKey));
    }

    [Fact]
    public void IsValid_RejectsKeysLongerThanDatabaseLimit()
    {
        Assert.False(PaymentGatewayKeyRules.IsValid(
            $"a{new string('b', PaymentGatewayKeyRules.MaxLength)}"));
    }
}
