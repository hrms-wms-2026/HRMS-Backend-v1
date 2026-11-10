using ONEVO.Infrastructure.Persistence.SqlServer;
using Xunit;

namespace ONEVO.Tests.Unit.Persistence;

public class RowVersionTokenTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(2001UL)]
    [InlineData(ulong.MaxValue)]
    public void Token_RoundTrips(ulong version)
    {
        Assert.True(RowVersionToken.TryParse(RowVersionToken.ToToken(version), out var parsed));
        Assert.Equal(version, parsed);
    }

    [Fact]
    public void ToToken_IsPlainDecimal()
        => Assert.Equal("2001", RowVersionToken.ToToken(2001UL));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    public void TryParse_RejectsJunk(string? token)
        => Assert.False(RowVersionToken.TryParse(token, out _));
}
