using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class EfLeavePolicyRepositoryTests
{
    [Fact]
    public async Task ListAsync_ReturnsTenantPoliciesWithTypeAndLegalEntityNames()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "ANNUAL");
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var policy = CreatePolicy(tenantId, "LK Policy");
        db.LeaveTypes.Add(leaveType);
        db.LegalEntities.Add(legalEntity);
        db.LeavePolicies.Add(policy);
        db.LeavePolicyLeaveTypes.Add(new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
            LeaveTypeId = leaveType.Id, AnnualEntitlementDays = 20m
        });
        db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
            LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
        });
        db.LeavePolicies.Add(CreatePolicy(otherTenantId, "Other Tenant Policy"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeavePolicyRepository(db);

        var results = await repo.ListAsync(tenantId, includeInactive: false, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("LK Policy", results[0].Policy.Name);
        Assert.Single(results[0].LeaveTypes);
        Assert.Equal("Annual Leave", results[0].LeaveTypes[0].LeaveTypeName);
        Assert.Single(results[0].LegalEntities);
        Assert.Equal("Acme Lanka", results[0].LegalEntities[0].LegalEntityName);
    }

    [Fact]
    public async Task ListActiveAssignmentConflictsAsync_ReturnsOnlyActiveSelectedLegalEntities()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var selected = CreateLegalEntity(tenantId, "Acme Lanka");
        var notSelected = CreateLegalEntity(tenantId, "Acme UK");
        var policy = CreatePolicy(tenantId, "Existing Policy");
        db.LegalEntities.AddRange(selected, notSelected);
        db.LeavePolicies.Add(policy);
        db.LeavePolicyLegalEntities.AddRange(
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
                LegalEntityId = selected.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            },
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
                LegalEntityId = notSelected.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeavePolicyRepository(db);

        var conflicts = await repo.ListActiveAssignmentConflictsAsync(
            tenantId, [selected.Id], CancellationToken.None);

        Assert.Single(conflicts);
        Assert.Equal(selected.Id, conflicts[0].LegalEntityId);
        Assert.Equal("Acme Lanka", conflicts[0].LegalEntityName);
        Assert.Equal("Existing Policy", conflicts[0].ActivePolicyName);
    }

    [Fact]
    public async Task AddAggregateWithReplacementAsync_DeactivatesOldAssignmentsAndCreatesNewAggregate()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "ANNUAL");
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var oldPolicy = CreatePolicy(tenantId, "Old Policy");
        db.LeaveTypes.Add(leaveType);
        db.LegalEntities.Add(legalEntity);
        db.LeavePolicies.Add(oldPolicy);
        db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = oldPolicy.Id,
            LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var newPolicy = CreatePolicy(tenantId, "New Policy");
        var typeRules = new[]
        {
            new LeavePolicyLeaveType
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = newPolicy.Id,
                LeaveTypeId = leaveType.Id, AnnualEntitlementDays = 20m
            }
        };
        var assignments = new[]
        {
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = newPolicy.Id,
                LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            }
        };

        var repo = new EfLeavePolicyRepository(db);

        await repo.AddAggregateWithReplacementAsync(
            newPolicy, typeRules, [], assignments, [legalEntity.Id], CancellationToken.None);

        var oldAssignment = await db.LeavePolicyLegalEntities
            .SingleAsync(x => x.LeavePolicyId == oldPolicy.Id);
        var newAssignment = await db.LeavePolicyLegalEntities
            .SingleAsync(x => x.LeavePolicyId == newPolicy.Id);

        Assert.False(oldAssignment.IsActive);
        Assert.True(newAssignment.IsActive);
        Assert.Equal(2, await db.LeavePolicies.CountAsync(x => x.TenantId == tenantId));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static LeavePolicy CreatePolicy(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Country = "LK",
        AccrualMethod = LeaveAccrualMethods.Annual,
        AccrualStart = LeaveAccrualStarts.Immediately,
        ProrationMethod = LeaveProrationMethods.CalendarDays,
        ApprovalMode = LeaveApprovalModes.AnyOne,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true
    };

    private static LeaveType CreateLeaveType(Guid tenantId, string name, string code) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Code = code,
        Category = LeaveTypeCategories.Annual,
        DefaultDaysPerYear = 20m,
        IsActive = true
    };

    private static LegalEntity CreateLegalEntity(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true
    };
}
