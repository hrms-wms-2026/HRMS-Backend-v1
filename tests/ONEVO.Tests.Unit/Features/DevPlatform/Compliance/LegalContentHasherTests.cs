using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class LegalContentHasherTests
{
    [Fact]
    public void ComputeHash_IsDeterministic_ForSameInput()
    {
        var first = LegalContentHasher.ComputeHash("<p>Hello</p>");
        var second = LegalContentHasher.ComputeHash("<p>Hello</p>");

        first.Should().Be(second);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_TrimsWhitespace_BeforeHashing()
    {
        var untrimmed = LegalContentHasher.ComputeHash("  <p>Hello</p>  ");
        var trimmed = LegalContentHasher.ComputeHash("<p>Hello</p>");

        untrimmed.Should().Be(trimmed);
    }

    [Fact]
    public void ComputeHash_MatchesKnownBootstrapTermsHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.TermsHtml);

        hash.Should().Be("20eafc33d68ca09f6c921b2ed37a67c9dfcaefb01af419f7cfd0a961d46ea696");
    }

    [Fact]
    public void ComputeHash_MatchesKnownBootstrapPrivacyHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml);

        hash.Should().Be("175134229efb70c740267a878bf0f2ae2954d9829fd6890f09c47801c3a6e1d7");
    }

    [Fact]
    public void ComputeHash_MatchesKnownGenericFallbackHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.GenericFallbackHtml);

        hash.Should().Be("cc9300cc121f9b3485dc29f8989624a5c340dbd7de7af964d3db538ab805fb24");
    }
}
