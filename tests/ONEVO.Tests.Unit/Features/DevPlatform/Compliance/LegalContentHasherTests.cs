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

        hash.Should().Be("ea4e1301bdb1a2e72aa01776adf4631d9e1c7493d32199486ec9ff2921f19be8");
    }

    [Fact]
    public void ComputeHash_MatchesKnownBootstrapPrivacyHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml);

        hash.Should().Be("f857164793343fc719e7a2879ba596d35ad42a110748d67d093f2df0464e6ca8");
    }

    [Fact]
    public void ComputeHash_MatchesKnownGenericFallbackHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.GenericFallbackHtml);

        hash.Should().Be("cc9300cc121f9b3485dc29f8989624a5c340dbd7de7af964d3db538ab805fb24");
    }
}
