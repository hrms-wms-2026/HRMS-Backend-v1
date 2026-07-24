using Microsoft.Extensions.Options;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.AgentGateway;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class NetworkEvidenceHasherTests
{
    private static NetworkEvidenceHasher CreateHasher(string key = "unit-test-master-key") =>
        new(Options.Create(new EncryptionOptions { MasterKey = key }));

    [Fact]
    public void Protect_DoubleHashesWithTenantIsolation()
    {
        const string localHash = "aabbccddeeff00112233445566778899";
        var firstTenant = CreateHasher().Protect(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), localHash);
        var secondTenant = CreateHasher().Protect(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), localHash);

        Assert.NotNull(firstTenant);
        Assert.Equal(64, firstTenant.Length);
        Assert.NotEqual(localHash, firstTenant);
        Assert.NotEqual(firstTenant, secondTenant);
        Assert.Matches("^[0-9a-f]{64}$", firstTenant);
    }

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("001122")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Protect_RejectsRawOrMalformedIdentifiers(string value)
    {
        Assert.Throws<ArgumentException>(() => CreateHasher().Protect(Guid.NewGuid(), value));
    }

    [Fact]
    public void Protect_AllowsMissingOptionalEvidence()
    {
        Assert.Null(CreateHasher().Protect(Guid.NewGuid(), null));
        Assert.Null(CreateHasher().Protect(Guid.NewGuid(), " "));
    }

    [Fact]
    public void Constructor_RejectsMissingMasterKey()
    {
        Assert.Throws<InvalidOperationException>(() => CreateHasher(" "));
    }
}
