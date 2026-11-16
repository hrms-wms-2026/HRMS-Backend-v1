using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Infrastructure.Identity.ExternalIdentity;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Identity;

public class GoogleIdTokenValidatorTests
{
    private readonly GoogleIdTokenValidator _sut = new(NullLogger<GoogleIdTokenValidator>.Instance);

    [Theory]
    [InlineData("fake.token.value")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.!!!.sig")]
    public async Task ValidateAsync_MalformedToken_ReturnsNullInsteadOfThrowing(string token)
    {
        var result = await _sut.ValidateAsync(token, "client-id.apps.googleusercontent.com");

        result.Should().BeNull();
    }
}
