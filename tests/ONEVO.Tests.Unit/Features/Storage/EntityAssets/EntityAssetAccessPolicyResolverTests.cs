using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class EntityAssetAccessPolicyResolverTests
{
    [Fact]
    public void Resolve_KnownOwnerType_ReturnsThePolicy()
    {
        var employeePolicy = new Mock<IEntityAssetAccessPolicy>().Object;
        var sut = new EntityAssetAccessPolicyResolver(
            new Dictionary<string, IEntityAssetAccessPolicy> { [EntityAssetOwnerTypes.Employee] = employeePolicy });

        sut.Resolve(EntityAssetOwnerTypes.Employee).Should().BeSameAs(employeePolicy);
    }

    [Fact]
    public void Resolve_UnknownOwnerType_ReturnsNull_DefaultDeny()
    {
        var sut = new EntityAssetAccessPolicyResolver(new Dictionary<string, IEntityAssetAccessPolicy>());

        sut.Resolve("some_unregistered_type").Should().BeNull();
    }
}
