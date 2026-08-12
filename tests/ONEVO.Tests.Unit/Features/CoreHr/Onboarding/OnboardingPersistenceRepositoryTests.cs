using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
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
    public async Task ListOnboardingRequests_DefaultsToPendingOnboardingActionTypeAndIsTenantScoped()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();

        var pending = await SeedFullRequestAsync(db, tenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding);
        await SeedFullRequestAsync(db, tenant, approvalStatus: "Approved", actionType: AccessGrantActionType.EmployeeOnboarding);
        await SeedFullRequestAsync(db, tenant, approvalStatus: "Rejected", actionType: AccessGrantActionType.EmployeeOnboarding);
        await SeedFullRequestAsync(db, tenant, approvalStatus: "Pending", actionType: "some_other_action_type");
        await SeedFullRequestAsync(db, other, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding);

        var repository = new EfAccessGrantRequestRepository(db);
        var (items, total) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25);

        total.Should().Be(1);
        items.Should().ContainSingle(x => x.AccessGrantRequestId == pending.RequestId);
    }

    [Fact]
    public async Task ListOnboardingRequests_ExcludesRequestsWithoutOnboardingDraftId()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var orphan = new AccessGrantRequest
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = null, UserId = null, OnboardingDraftId = null,
            ActionType = AccessGrantActionType.EmployeeOnboarding, TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(),
            PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(), ApprovalStatus = "Pending",
            RequestedByUserId = Guid.NewGuid(), RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow,
        };
        db.AccessGrantRequests.Add(orphan);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var repository = new EfAccessGrantRequestRepository(db);
        var (items, total) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25);

        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task ListOnboardingRequests_StatusFilterMatchesOnlyThatStatus(string status)
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var pending = await SeedFullRequestAsync(db, tenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding);
        var approved = await SeedFullRequestAsync(db, tenant, approvalStatus: "Approved", actionType: AccessGrantActionType.EmployeeOnboarding);
        var rejected = await SeedFullRequestAsync(db, tenant, approvalStatus: "Rejected", actionType: AccessGrantActionType.EmployeeOnboarding);
        var expected = status switch { "Pending" => pending, "Approved" => approved, _ => rejected };

        var repository = new EfAccessGrantRequestRepository(db);
        var (items, total) = await repository.ListOnboardingRequestsAsync(
            tenant, status, AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25);

        total.Should().Be(1);
        items.Should().ContainSingle(x => x.AccessGrantRequestId == expected.RequestId);
    }

    [Fact]
    public async Task ListOnboardingRequests_PaginatesInRequestedAtDescendingOrder()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = await SeedFullRequestAsync(db, tenant, requestedAt: now.AddMinutes(-30));
        var second = await SeedFullRequestAsync(db, tenant, requestedAt: now.AddMinutes(-20));
        var third = await SeedFullRequestAsync(db, tenant, requestedAt: now.AddMinutes(-10));

        var repository = new EfAccessGrantRequestRepository(db);
        var (page1, total1) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 2);
        var (page2, total2) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 2, 2);

        total1.Should().Be(3); total2.Should().Be(3);
        page1.Should().HaveCount(2);
        page1.Select(x => x.AccessGrantRequestId).Should().Equal(third.RequestId, second.RequestId);
        page2.Should().ContainSingle(x => x.AccessGrantRequestId == first.RequestId);
    }

    [Fact]
    public async Task ListOnboardingRequests_LegalEntityAndRoleFiltersNarrowResults()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var match = await SeedFullRequestAsync(db, tenant);
        var otherLegalEntity = await SeedFullRequestAsync(db, tenant);

        var repository = new EfAccessGrantRequestRepository(db);
        var (byLegalEntity, _) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, match.LegalEntityId, null, null, 1, 25);
        byLegalEntity.Should().ContainSingle(x => x.AccessGrantRequestId == match.RequestId);

        var (byRole, _) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, match.RequestedRoleId, null, 1, 25);
        byRole.Should().ContainSingle(x => x.AccessGrantRequestId == match.RequestId);

        var _ = otherLegalEntity;
    }

    [Theory]
    [InlineData("jane")]
    [InlineData("work.email")]
    [InlineData("engineer")]
    [InlineData("hr manager")]
    public async Task ListOnboardingRequests_SearchMatchesDisplayNameEmailPositionOrRole(string term)
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var match = await SeedFullRequestAsync(
            db, tenant, firstName: "Jane", lastName: "Doe", workEmail: "jane.doe.work.email@example.com",
            positionName: "Software Engineer", roleName: "HR Manager");
        await SeedFullRequestAsync(
            db, tenant, firstName: "Alex", lastName: "Smith", workEmail: "alex.smith@example.com",
            positionName: "Recruiter", roleName: "Payroll Admin");

        var repository = new EfAccessGrantRequestRepository(db);
        var (items, total) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, term, 1, 25);

        total.Should().Be(1);
        items.Should().ContainSingle(x => x.AccessGrantRequestId == match.RequestId);
    }

    [Fact]
    public async Task ListOnboardingRequests_PopulatesDisplayFieldsFromJoinedEntities()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var seeded = await SeedFullRequestAsync(
            db, tenant, firstName: "Jane", lastName: "Doe", workEmail: "jane.doe@example.com",
            positionName: "Software Engineer", roleName: "HR Manager", legalEntityName: "Acme Corp", departmentName: "Engineering",
            requesterFirstName: "Riya", requesterLastName: "Starter");

        var repository = new EfAccessGrantRequestRepository(db);
        var (items, _) = await repository.ListOnboardingRequestsAsync(
            tenant, "Pending", AccessGrantActionType.EmployeeOnboarding, null, null, null, 1, 25);

        var item = items.Should().ContainSingle().Subject;
        item.OnboardingDraftId.Should().Be(seeded.DraftId);
        item.DisplayName.Should().Be("Jane Doe");
        item.WorkEmail.Should().Be("jane.doe@example.com");
        item.TargetPositionName.Should().Be("Software Engineer");
        item.RequestedRoleName.Should().Be("HR Manager");
        item.LegalEntityName.Should().Be("Acme Corp");
        item.DepartmentName.Should().Be("Engineering");
        item.RequestedByName.Should().Be("Riya Starter");
        item.DecidedByName.Should().BeNull();
        item.DecidedAt.Should().BeNull();
        item.DraftStatus.Should().Be(OnboardingDraftStatus.WaitingForPositionApproval);
    }

    private static async Task<SeededRequest> SeedFullRequestAsync(
        ApplicationDbContext db, Guid tenantId, string approvalStatus = "Pending",
        string actionType = "", string? firstName = null, string? lastName = null, string? workEmail = null,
        string? positionName = null, string? roleName = null, string? legalEntityName = null, string? departmentName = null,
        string? requesterFirstName = null, string? requesterLastName = null, DateTimeOffset? requestedAt = null)
    {
        if (string.IsNullOrEmpty(actionType))
        {
            actionType = AccessGrantActionType.EmployeeOnboarding;
        }

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = legalEntityName ?? "Legal Entity " + Guid.NewGuid(),
            CountryCode = "US", CurrencyCode = "USD",
        };
        var department = new Department
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntity.Id, Name = departmentName ?? "Department " + Guid.NewGuid(),
        };
        var position = new Position
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntity.Id, DepartmentId = department.Id,
            Name = positionName ?? "Position " + Guid.NewGuid(),
        };
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = roleName ?? "Role " + Guid.NewGuid() };
        var requester = new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FirstName = requesterFirstName ?? "Requester", LastName = requesterLastName ?? "User",
            Email = $"requester-{Guid.NewGuid()}@example.com",
        };
        var draft = new OnboardingDraft
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FirstName = firstName ?? "Firstname", LastName = lastName ?? "Lastname",
            WorkEmail = workEmail ?? $"work-{Guid.NewGuid()}@example.com", LegalEntityId = legalEntity.Id, DepartmentId = department.Id,
            PositionId = position.Id, EmploymentType = "full_time", StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = OnboardingDraftStatus.WaitingForPositionApproval, DraftReason = OnboardingDraftReason.WaitingForPositionApproval,
            StartedById = requester.Id,
        };
        var request = new AccessGrantRequest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = null, UserId = null, OnboardingDraftId = draft.Id,
            ActionType = actionType, TargetPositionId = position.Id, TargetDepartmentId = department.Id,
            PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = role.Id, ApprovalStatus = approvalStatus,
            RequestedByUserId = requester.Id, RequestedAt = requestedAt ?? DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow,
        };

        db.LegalEntities.Add(legalEntity);
        db.Departments.Add(department);
        db.Positions.Add(position);
        db.Roles.Add(role);
        db.Users.Add(requester);
        db.OnboardingDrafts.Add(draft);
        db.AccessGrantRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new SeededRequest(request.Id, draft.Id, legalEntity.Id, role.Id);
    }

    private sealed record SeededRequest(Guid RequestId, Guid DraftId, Guid LegalEntityId, Guid RequestedRoleId);

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
