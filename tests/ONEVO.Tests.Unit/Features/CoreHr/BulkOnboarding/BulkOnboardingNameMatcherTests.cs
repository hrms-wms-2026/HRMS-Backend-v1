using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingNameMatcherTests
{
    [Theory]
    [InlineData("Human Resources", "Human Resources", "exact")]
    [InlineData("  human   resources ", "Human Resources", "exact")]
    [InlineData("Human Resorces", "Human Resources", "high")]
    [InlineData("Eng", "Engineering", "medium")]
    [InlineData("Sofware Engineer", "Software Engineer", "high")]
    [InlineData("Sales Dept", "Sales", "exact")]
    [InlineData("Sales Department", "Sales", "exact")]
    public void FindBest_ReturnsExpectedConfidence(string imported, string candidate, string expectedConfidence)
    {
        var match = BulkOnboardingNameMatcher.FindBest(imported, [candidate]);

        Assert.NotNull(match);
        Assert.Equal(candidate, match.Label);
        Assert.Equal(expectedConfidence, match.Confidence);
    }

    [Fact]
    public void FindBest_LowConfidenceRandom_ReturnsNull()
    {
        var match = BulkOnboardingNameMatcher.FindBest("Zzqx", ["Human Resources", "Engineering", "Sales"]);

        Assert.Null(match);
    }

    [Fact]
    public void FindBest_PrefersExactOverFuzzy()
    {
        var match = BulkOnboardingNameMatcher.FindBest(
            "Sales",
            ["Sales Operations", "Sales", "Sails"]);

        Assert.NotNull(match);
        Assert.Equal("Sales", match.Label);
        Assert.Equal("exact", match.Confidence);
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndSuffixes()
    {
        Assert.Equal("sales", BulkOnboardingNameMatcher.Normalize("Sales Dept"));
        Assert.Equal("human resources", BulkOnboardingNameMatcher.Normalize("  Human   Resources  "));
    }
}
