using FluentAssertions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class TenantContextAccessorTests
{
    private static TenantContextAccessor Build() => new();

    [Fact]
    public void DefaultState_IsNotResolved_SystemMode_EmptyTenantId()
    {
        var sut = Build();
        sut.IsResolved.Should().BeFalse();
        sut.ContextMode.Should().Be(TenantContextMode.System);
        sut.TenantId.Should().Be(Guid.Empty);
        sut.Slug.Should().BeNull();
        sut.Status.Should().BeNull();
    }

    [Fact]
    public void Resolve_SetsAllProperties_AndTenantMode()
    {
        var sut = Build();
        var entry = new TenantRegistryEntry(Guid.NewGuid(), "acme", TenantStatus.Active, "starter");

        ((IWritableTenantContext)sut).Resolve(entry);

        sut.IsResolved.Should().BeTrue();
        sut.ContextMode.Should().Be(TenantContextMode.Tenant);
        sut.TenantId.Should().Be(entry.TenantId);
        sut.Slug.Should().Be("acme");
        sut.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void SetAdminMode_ClearsTenantProperties_SetsAdminMode()
    {
        var sut = Build();
        ((IWritableTenantContext)sut).SetAdminMode();

        sut.ContextMode.Should().Be(TenantContextMode.Admin);
        sut.IsResolved.Should().BeFalse();
        sut.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SetSystemMode_LeavesContextAsSystem()
    {
        var sut = Build();
        ((IWritableTenantContext)sut).SetSystemMode();

        sut.ContextMode.Should().Be(TenantContextMode.System);
        sut.IsResolved.Should().BeFalse();
    }
}
