using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class EmployeeNumberRulesTests
{
    [Theory]
    [InlineData("DAPI-0001", true)]
    [InlineData("ACME_7", true)]
    [InlineData("a-b_c1", true)]
    [InlineData("DAPI 0001", false)]
    [InlineData("DAPI/0001", false)]
    [InlineData("", false)]
    public void IsValidFormat_MatchesAllowedCharset(string value, bool expected)
        => Assert.Equal(expected, EmployeeNumberRules.IsValidFormat(value));

    [Fact]
    public void FormatSuggested_UsesCompanyCodeAndPaddedSequence()
        => Assert.Equal("DAPI-0005", EmployeeNumberRules.FormatSuggested("DAPI", 5));

    [Fact]
    public void TryNormalizePrefix_RejectsEmptyCompanyCode()
    {
        Assert.False(EmployeeNumberRules.TryNormalizePrefix("  ", out _, out var error));
        Assert.Contains("company code", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseSequence_ReadsNumericSuffix()
    {
        Assert.Equal(5, EmployeeNumberRules.TryParseSequence("DAPI-0005", "DAPI"));
        Assert.Null(EmployeeNumberRules.TryParseSequence("ACME-0005", "DAPI"));
    }
}
