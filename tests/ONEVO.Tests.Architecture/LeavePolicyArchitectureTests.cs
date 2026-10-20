using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Domain.Common;
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
    [Theory]
    [InlineData(typeof(LeavePolicy))]
    [InlineData(typeof(LeavePolicyVersion))]
    [InlineData(typeof(LeavePolicyAssignment))]
    [InlineData(typeof(LeavePolicyBlackoutPeriod))]
    public void LeavePolicyEntities_AreTenantOwned(Type type)
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(type));
    }

    [Fact]
    public void Model_LeavePolicy_HasSingleLeaveType()
    {
        using var context = CreateModelInspectionContext();
        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LeavePolicy));
        var property = entityType.FindProperty(nameof(LeavePolicy.LeaveTypeId));

        Assert.NotNull(property);
        Assert.Equal("leave_type_id", property!.GetColumnName(StoreObjectIdentifier.Table("leave_policies")));
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void Model_LeavePolicyVersion_VersionNumberAndEffectiveDateAreUniquePerPolicy()
    {
        using var context = CreateModelInspectionContext();
        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LeavePolicyVersion));

        Assert.Equal("leave_policy_versions", entityType.GetTableName());
        Assert.Contains(entityType.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(LeavePolicyVersion.LeavePolicyId), nameof(LeavePolicyVersion.VersionNumber)]));
        Assert.Contains(entityType.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(LeavePolicyVersion.LeavePolicyId), nameof(LeavePolicyVersion.EffectiveFrom)]));
    }

    [Fact]
    public void Model_LeavePolicyAssignment_OneOpenPolicyPerLeaveTypePerTarget()
    {
        using var context = CreateModelInspectionContext();
        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LeavePolicyAssignment));

        Assert.Equal("leave_policy_assignments", entityType.GetTableName());
        var unique = Assert.Single(entityType.GetIndexes(), i => i.IsUnique);
        Assert.Equal(
            [nameof(LeavePolicyAssignment.TenantId), nameof(LeavePolicyAssignment.TargetKind),
             nameof(LeavePolicyAssignment.TargetId), nameof(LeavePolicyAssignment.LeaveTypeId)],
            unique.Properties.Select(p => p.Name));
        Assert.Equal("effective_to IS NULL", unique.GetFilter());
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
