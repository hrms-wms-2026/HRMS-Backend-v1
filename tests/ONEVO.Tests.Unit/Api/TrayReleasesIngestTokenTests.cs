using ONEVO.Api.Configuration;

namespace ONEVO.Tests.Unit.Api;

public sealed class TrayReleasesIngestTokenTests
{
    [Fact]
    public void Valid_WhenTokensMatch() =>
        Assert.True(IngestTokenValidator.IsValid("s3cret-token-value", "s3cret-token-value"));

    [Theory]
    [InlineData("s3cret-token-value", "wrong")]
    [InlineData("s3cret-token-value", "")]
    [InlineData("s3cret-token-value", null)]
    public void Invalid_WhenPresentedTokenWrongOrMissing(string configured, string? presented) =>
        Assert.False(IngestTokenValidator.IsValid(configured, presented));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_WhenNoTokenConfigured_EvenIfPresentedIsEmpty(string? configured) =>
        // Ingest is DISABLED unless a token is configured; an empty==empty match must not pass.
        Assert.False(IngestTokenValidator.IsValid(configured, configured ?? ""));
}
