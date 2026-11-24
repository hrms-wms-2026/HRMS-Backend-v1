using FluentAssertions;
using ONEVO.Application.Common.Caching;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Caching;

/// <summary>Improvements task 22: cached entities are handed out as copies; saves drop the right entries.</summary>
public sealed class ReferenceDataCopyTests
{
    [Fact]
    public void A_copy_can_be_changed_without_touching_the_cached_entity()
    {
        var cached = new LeaveType { Id = Guid.NewGuid(), Name = "Annual", AcceptedDocumentTypes = ["pdf"] };

        var copy = ReferenceDataCopy.One(cached);
        copy.Name = "Changed";
        copy.AcceptedDocumentTypes[0] = "png";

        copy.Should().NotBeSameAs(cached);
        cached.Name.Should().Be("Annual");
        cached.AcceptedDocumentTypes.Should().Equal("pdf");
        copy.Id.Should().Be(cached.Id);
    }

    [Fact]
    public void Child_collections_are_copied_and_point_back_at_the_new_parent()
    {
        var cached = new ClockInPolicy { Id = Guid.NewGuid(), Name = "Policy", DepartmentIds = [Guid.NewGuid()] };
        var rule = new ClockInLateDeductionRule { Id = Guid.NewGuid(), ClockInPolicyId = cached.Id, LateArrivalMinute = 10, ClockInPolicy = cached };
        cached.LateDeductionRules.Add(rule);

        var copy = ReferenceDataCopy.One(cached);
        copy.LateDeductionRules.Single().LateArrivalMinute = 99;
        copy.LateDeductionRules.Add(new ClockInLateDeductionRule());
        copy.DepartmentIds![0] = Guid.Empty;

        cached.LateDeductionRules.Should().ContainSingle().Which.LateArrivalMinute.Should().Be(10);
        copy.LateDeductionRules.First().ClockInPolicy.Should().BeSameAs(copy);
        copy.LateDeductionRules.First().Should().NotBeSameAs(rule);
        cached.DepartmentIds![0].Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Pending_domain_events_are_not_shared_between_copies()
    {
        var cached = new TaskCategory { Id = Guid.NewGuid(), Name = "General" };
        var field = typeof(ONEVO.Domain.Common.BaseEntity).GetField(
            "_domainEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var copy = ReferenceDataCopy.One(cached);

        field.GetValue(copy).Should().NotBeSameAs(field.GetValue(cached));
    }

    [Fact]
    public void Empty_navigation_collections_get_their_own_list()
    {
        var cached = new Permission { Id = Guid.NewGuid(), Code = "leave:read", Module = "leave" };

        var copy = ReferenceDataCopy.One(cached);
        copy.RolePermissions.Add(new RolePermission());

        cached.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public void A_list_copy_has_the_same_rows_in_the_same_order()
    {
        IReadOnlyList<ProjectCategory> cached =
            [new() { Id = Guid.NewGuid(), Name = "A" }, new() { Id = Guid.NewGuid(), Name = "B" }];

        var copies = ReferenceDataCopy.All(cached);

        copies.Select(c => c.Id).Should().Equal(cached.Select(c => c.Id));
        copies.Should().NotContain(c => cached.Contains(c));
        ReferenceDataCopy.OneOrNull<HolidayCalendarSettings>(null).Should().BeNull();
    }

    [Fact]
    public void Each_cached_entity_drops_its_own_tenants_entry()
    {
        var tenantId = Guid.NewGuid();

        Prefixes(new TenantSubscription { TenantId = tenantId }).Should().BeEquivalentTo(
            Tenant(tenantId, ReferenceDataCacheKeys.ActiveModules), Tenant(tenantId, ReferenceDataCacheKeys.AssignablePermissions));
        Prefixes(new LeaveType { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.LeaveTypes));
        Prefixes(new WorkMode { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.WorkModes));
        Prefixes(new ClockInPolicy { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.ClockInPolicies));
        Prefixes(new ClockInLateDeductionRule { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.ClockInPolicies));
        Prefixes(new HolidayCalendarSettings { TenantId = tenantId })
            .Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.HolidayCalendarSettings));
        Prefixes(new TaskCategory { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.TaskCategories));
        Prefixes(new ProjectCategory { TenantId = tenantId }).Should().Equal(Tenant(tenantId, ReferenceDataCacheKeys.ProjectCategories));
    }

    [Fact]
    public void Platform_wide_entities_drop_the_lookup_for_every_tenant()
    {
        Prefixes(new Permission()).Should().BeEquivalentTo(
            ReferenceDataCacheKeys.NamePrefix(ReferenceDataCacheKeys.UniversalPermissions),
            ReferenceDataCacheKeys.NamePrefix(ReferenceDataCacheKeys.AssignablePermissions));
        Prefixes(new ModuleCatalogItem()).Should().Equal(ReferenceDataCacheKeys.NamePrefix(ReferenceDataCacheKeys.ActiveModuleCatalog));
        Prefixes(new Role { TenantId = Guid.NewGuid() }).Should().BeEmpty();
    }

    private static List<string> Prefixes(object entity) => ReferenceDataCacheInvalidationInterceptor.PrefixesFor(entity).ToList();

    private static string Tenant(Guid tenantId, string name) => ReferenceDataCacheKeys.TenantPrefix(tenantId, name);
}
