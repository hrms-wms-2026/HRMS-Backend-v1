using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class OnboardingPersistenceRepositoryTests
{
    [Fact]
    public async Task AccessGrantRequest_PendingLookupIsTenantScopedAndPreservesIds()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var draftId = Guid.NewGuid(); var position = Guid.NewGuid(); var template = Guid.NewGuid();
        var request = new AccessGrantRequest { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = null, UserId = null, OnboardingDraftId = draftId, TargetPositionId = position, TargetDepartmentId = Guid.NewGuid(), PositionAccessTemplateId = template, RequestedRoleId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ActionType = "Onboarding", RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow };
        var repository = new EfAccessGrantRequestRepository(db);
        await repository.AddAsync(request); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();
        (await repository.GetPendingByDraftAsync(tenant, draftId, position, template)).Should().NotBeNull();
        (await repository.GetPendingByDraftAsync(Guid.NewGuid(), draftId, position, template)).Should().BeNull();
    }

    [Fact]
    public async Task AccessGrantRequest_TrackedByIdLookupIsTenantScoped()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var draftId = Guid.NewGuid();
        var request = new AccessGrantRequest { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = null, UserId = null, OnboardingDraftId = draftId, TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(), PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ActionType = "Onboarding", RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow };
        var repository = new EfAccessGrantRequestRepository(db);
        await repository.AddAsync(request); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();
        (await repository.GetTrackedByIdAsync(tenant, request.Id)).Should().NotBeNull();
        (await repository.GetTrackedByIdAsync(Guid.NewGuid(), request.Id)).Should().BeNull();
        (await repository.GetTrackedByIdAsync(tenant, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task AccessGrantRequest_AnyPendingByDraft_OnlyMatchesPendingAndIsTenantScoped()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var draftId = Guid.NewGuid();
        var rejected = new AccessGrantRequest { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = null, UserId = null, OnboardingDraftId = draftId, TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(), PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ActionType = "Onboarding", ApprovalStatus = "Rejected", RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow };
        var repository = new EfAccessGrantRequestRepository(db);
        await repository.AddAsync(rejected); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();

        // A rejected-only history for this draft must not read as "still pending".
        (await repository.AnyPendingByDraftAsync(tenant, draftId)).Should().BeFalse();
        (await repository.AnyPendingByDraftAsync(Guid.NewGuid(), draftId)).Should().BeFalse();

        var pending = new AccessGrantRequest { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = null, UserId = null, OnboardingDraftId = draftId, TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(), PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ActionType = "Onboarding", RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow };
        await repository.AddAsync(pending); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();

        (await repository.AnyPendingByDraftAsync(tenant, draftId)).Should().BeTrue();
        (await repository.AnyPendingByDraftAsync(Guid.NewGuid(), draftId)).Should().BeFalse();
    }

    [Fact]
    public async Task ChecklistTemplate_OnlyLoadsActiveTenantScopedOnboardingScopeMatch()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var department = Guid.NewGuid(); var template = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Starter", TemplateType = "onboarding", DepartmentId = department, TasksJson = "[]", IsActive = true };
        db.ChecklistTemplates.AddRange(template, new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Inactive", TemplateType = "onboarding", TasksJson = "[]", IsActive = false }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var repository = new EfChecklistTemplateRepository(db);
        (await repository.GetActiveOnboardingAsync(tenant, template.Id, department)).Should().NotBeNull();
        (await repository.GetActiveOnboardingAsync(tenant, template.Id, Guid.NewGuid())).Should().BeNull();
        (await repository.GetActiveOnboardingAsync(Guid.NewGuid(), template.Id, department)).Should().BeNull();
    }

    [Fact]
    public async Task EmployeeTasks_InstantiateEditedJsonInSequenceAndRejectInvalidJson()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var template = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Starter", TemplateType = "onboarding", TasksJson = "[]" };
        var json = $"[{{\"title\":\"Second\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{user}\",\"dueDate\":\"2026-09-01\",\"sequence\":2}},{{\"title\":\"First\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{user}\",\"dueDate\":\"2026-08-01\",\"sequence\":1}}]";
        var repository = new EfEmployeeChecklistTaskRepository(db);
        var tasks = await repository.InstantiateAsync(template, Guid.NewGuid(), json);
        tasks.Select(x => x.Sequence).Should().Equal(2, 1); tasks.Should().OnlyContain(x => x.TenantId == tenant && x.TemplateId == template.Id && x.LifecycleType == "onboarding");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.InstantiateAsync(template, Guid.NewGuid(), "{}"));
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>(); var currentUser = new Mock<ICurrentUser>(); var publisher = new Mock<IPublisher>(); var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(options, new AuditableEntityInterceptor(currentUser.Object, clock.Object), new SoftDeleteInterceptor(clock.Object), new DomainEventDispatchInterceptor(publisher.Object), tenant.Object);
    }
}
