using ONEVO.Infrastructure.Identity.Tokens;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public sealed class SecureTokenGeneratorTests
{
    private readonly SecureTokenGenerator _sut = new();

    [Fact]
    public void GenerateUrlSafeOpaqueToken_DoesNotContainBase64PathUnsafeCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var token = _sut.GenerateUrlSafeOpaqueToken();

            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('=', token);
        }
    }

    [Fact]
    public void GenerateUrlSafeOpaqueToken_OnlyContainsUrlSafeAlphabet()
    {
        var token = _sut.GenerateUrlSafeOpaqueToken();

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void GenerateUrlSafeOpaqueToken_ProducesUniqueValuesAcrossCalls()
    {
        var first = _sut.GenerateUrlSafeOpaqueToken();
        var second = _sut.GenerateUrlSafeOpaqueToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateUrlSafeOpaqueToken_HasAtLeastAsMuchEntropyAsGenerateOpaqueToken()
    {
        var urlSafeToken = _sut.GenerateUrlSafeOpaqueToken();
        var base64Token = _sut.GenerateOpaqueToken();

        // Both are generated from 64 random bytes; Base64Url without padding is a few
        // characters shorter than standard Base64 with padding, but decodes back to the
        // same byte length.
        Assert.True(urlSafeToken.Length >= base64Token.Length - 4);
    }
}
