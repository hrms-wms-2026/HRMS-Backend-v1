using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Helpers;

public sealed class DeterministicGuidTests
{
    private static readonly Guid Namespace = Guid.Parse("6f1f9b2a-6c1e-4b7a-9c2e-8f6a1d2b3c4d");

    [Fact]
    public void Create_SameInputs_ReturnsSameGuid()
    {
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Create_DifferentName_ReturnsDifferentGuid()
    {
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(Namespace, "master-1|2026-09-08T09:00:00Z");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Create_DifferentNamespace_ReturnsDifferentGuid()
    {
        var otherNamespace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(otherNamespace, "master-1|2026-09-01T09:00:00Z");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Create_ReturnsVersion5Variant2Guid()
    {
        var guid = DeterministicGuid.Create(Namespace, "any-name");
        var bytes = guid.ToByteArray();

        Assert.Equal(5, bytes[7] >> 4); // version nibble
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC 4122 variant
    }
}
