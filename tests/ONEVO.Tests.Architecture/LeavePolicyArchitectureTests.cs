using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeavePolicyArchitectureTests
{
    [Fact]
    public void LeavePolicy_IsTenantOwned()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(LeavePolicy)));
    }

    [Fact]
    public void LeavePolicy_HasAccrualMethodProperty()
    {
        var property = typeof(LeavePolicy).GetProperty(nameof(LeavePolicy.AccrualMethod));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }

    [Fact]
    public void Model_LeavePolicy_AccrualMethod_MapsToRequiredColumn()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LeavePolicy));
        var property = entityType.FindProperty(nameof(LeavePolicy.AccrualMethod));
        var table = StoreObjectIdentifier.Table("leave_policies");

        Assert.NotNull(property);
        Assert.Equal("accrual_method", property!.GetColumnName(table));
        Assert.False(property.IsNullable);
        Assert.Equal(20, property.GetMaxLength());
    }

    [Fact]
    public void LeaveAccrualMethods_UsesStringConstants()
    {
        Assert.Equal("annual", LeaveAccrualMethods.Annual);
        Assert.Equal("monthly", LeaveAccrualMethods.Monthly);
        Assert.Equal("daily", LeaveAccrualMethods.Daily);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"leave-policy-arch-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
