using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;
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
    public void AccessGrantRequest_XminIsConcurrencyToken()
    {
        using var db = BuildDb();
        var xmin = db.Model.FindEntityType(typeof(AccessGrantRequest))!.FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin!.IsConcurrencyToken.Should().BeTrue();
        xmin.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
        xmin.ClrType.Should().Be(typeof(uint?));
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
    public async Task AccessGrantRequest_AnyPendingByEmployee_OnlyMatchesPendingPositionChange()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var rejected = new AccessGrantRequest
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employeeId, UserId = null,
            TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(),
            PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(), ActionType = AccessGrantActionType.PositionChange,
            ApprovalStatus = "Rejected", RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow,
        };
        var repository = new EfAccessGrantRequestRepository(db);
        await repository.AddAsync(rejected); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();

        (await repository.AnyPendingByEmployeeAsync(tenant, employeeId)).Should().BeFalse();

        var pending = new AccessGrantRequest
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employeeId, UserId = null,
            TargetPositionId = Guid.NewGuid(), TargetDepartmentId = Guid.NewGuid(),
            PositionAccessTemplateId = Guid.NewGuid(), RequestedRoleId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(), ActionType = AccessGrantActionType.PositionChange,
            RequestedAt = DateTimeOffset.UtcNow, EffectiveFrom = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(pending); await repository.SaveChangesAsync(); db.ChangeTracker.Clear();

        (await repository.AnyPendingByEmployeeAsync(tenant, employeeId)).Should().BeTrue();
        (await repository.AnyPendingByEmployeeAsync(Guid.NewGuid(), employeeId)).Should().BeFalse();
        (await repository.AnyPendingByEmployeeAsync(tenant, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task ListPending_ReturnsOnlyPendingRows_WithResolvedNames()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        var onboarding = await SeedFullRequestAsync(
            db, tenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding,
            positionName: "Software Engineer", requesterFirstName: "Riya", requesterLastName: "Starter");
        await SeedFullRequestAsync(db, tenant, approvalStatus: "Approved", actionType: AccessGrantActionType.EmployeeOnboarding);
        await SeedFullRequestAsync(db, tenant, approvalStatus: "Rejected", actionType: AccessGrantActionType.EmployeeOnboarding);
        await SeedFullRequestAsync(db, otherTenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding);

        var changePosition = new Position
        {
            Id = Guid.NewGuid(), TenantId = tenant, Name = "Engineering Manager",
        };
        var employee = new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = tenant, UserId = Guid.NewGuid(), EmployeeNumber = "E-1",
            FirstName = "Jane", LastName = "Doe", Email = "jane.doe@example.com",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var changeRequester = new User
        {
            Id = Guid.NewGuid(), TenantId = tenant, FirstName = "Riya", LastName = "Starter",
            Email = $"requester-{Guid.NewGuid()}@example.com",
        };
        var positionChange = new AccessGrantRequest
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee.Id, UserId = employee.UserId,
            ActionType = AccessGrantActionType.PositionChange, TargetPositionId = changePosition.Id,
            TargetDepartmentId = Guid.NewGuid(), PositionAccessTemplateId = Guid.NewGuid(),
            RequestedRoleId = Guid.NewGuid(), ApprovalStatus = "Pending", ChangeReason = "Promotion",
            RequestedByUserId = changeRequester.Id, RequestedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            EffectiveFrom = DateTimeOffset.UtcNow,
        };
        db.Positions.Add(changePosition);
        db.Employees.Add(employee);
        db.Users.Add(changeRequester);
        db.AccessGrantRequests.Add(positionChange);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfAccessGrantRequestRepository(db);
        var items = await repository.ListPendingAsync(tenant, excludeRequestedByUserId: Guid.Empty);

        items.Should().HaveCount(2);
        items.Should().OnlyContain(x => x.Id == onboarding.RequestId || x.Id == positionChange.Id);

        var onboardingItem = items.Should().ContainSingle(x => x.Id == onboarding.RequestId).Subject;
        onboardingItem.ActionType.Should().Be(AccessGrantActionType.EmployeeOnboarding);
        onboardingItem.EmployeeName.Should().BeNull();
        onboardingItem.InvitedFullName.Should().Be("Firstname Lastname");
        onboardingItem.TargetPositionName.Should().Be("Software Engineer");
        onboardingItem.RequestedByName.Should().Be("Riya Starter");
        onboardingItem.ChangeReason.Should().BeNull();

        var changeItem = items.Should().ContainSingle(x => x.Id == positionChange.Id).Subject;
        changeItem.ActionType.Should().Be(AccessGrantActionType.PositionChange);
        changeItem.EmployeeName.Should().Be("Jane Doe");
        changeItem.InvitedFullName.Should().BeNull();
        changeItem.TargetPositionName.Should().Be("Engineering Manager");
        changeItem.ChangeReason.Should().Be("Promotion");
        changeItem.RequestedByName.Should().Be("Riya Starter");
    }

    [Fact]
    public async Task ListPending_ExcludesRowsSubmittedByCaller()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        var own = await SeedFullRequestAsync(
            db, tenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding,
            requesterFirstName: "Self", requesterLastName: "Submitter");
        db.AccessGrantRequests.First(x => x.Id == own.RequestId).RequestedByUserId = callerId;
        await db.SaveChangesAsync();

        var other = await SeedFullRequestAsync(
            db, tenant, approvalStatus: "Pending", actionType: AccessGrantActionType.EmployeeOnboarding,
            requesterFirstName: "Other", requesterLastName: "Person");
        db.ChangeTracker.Clear();

        var repository = new EfAccessGrantRequestRepository(db);
        var items = await repository.ListPendingAsync(tenant, excludeRequestedByUserId: callerId);

        items.Should().ContainSingle(x => x.Id == other.RequestId);
        items.Should().NotContain(x => x.Id == own.RequestId);
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
    public async Task GetActiveOnboardingAsync_MatchesByLegalEntityAndScopePriority()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var legalEntity = Guid.NewGuid(); var department = Guid.NewGuid(); var position = Guid.NewGuid();
        var companyTemplate = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Company Default", TemplateType = "onboarding", LegalEntityId = legalEntity, TasksJson = "[]", IsActive = true };
        var departmentTemplate = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Dept", TemplateType = "onboarding", LegalEntityId = legalEntity, DepartmentId = department, TasksJson = "[]", IsActive = true };
        var positionTemplate = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Position", TemplateType = "onboarding", LegalEntityId = legalEntity, DepartmentId = department, PositionId = position, TasksJson = "[]", IsActive = true };
        var wrongCompanyTemplate = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Other Co", TemplateType = "onboarding", LegalEntityId = Guid.NewGuid(), TasksJson = "[]", IsActive = true };
        db.ChecklistTemplates.AddRange(companyTemplate, departmentTemplate, positionTemplate, wrongCompanyTemplate);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var repository = new EfChecklistTemplateRepository(db);

        (await repository.GetActiveOnboardingAsync(tenant, companyTemplate.Id, legalEntity, department, position)).Should().NotBeNull();
        (await repository.GetActiveOnboardingAsync(tenant, departmentTemplate.Id, legalEntity, department, position)).Should().NotBeNull();
        (await repository.GetActiveOnboardingAsync(tenant, positionTemplate.Id, legalEntity, department, position)).Should().NotBeNull();
        (await repository.GetActiveOnboardingAsync(tenant, positionTemplate.Id, legalEntity, department, Guid.NewGuid())).Should().BeNull();
        (await repository.GetActiveOnboardingAsync(tenant, wrongCompanyTemplate.Id, legalEntity, department, position)).Should().BeNull();
    }

    [Fact]
    public async Task ListOnboardingMatchesAsync_OrdersPositionThenDepartmentThenCompany_AndExcludesOffboarding()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var legalEntity = Guid.NewGuid(); var department = Guid.NewGuid(); var position = Guid.NewGuid();
        var company = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Company", TemplateType = "onboarding", LegalEntityId = legalEntity, TasksJson = "[]", IsActive = true };
        var dept = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Dept", TemplateType = "onboarding", LegalEntityId = legalEntity, DepartmentId = department, TasksJson = "[]", IsActive = true };
        var pos = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Pos", TemplateType = "onboarding", LegalEntityId = legalEntity, DepartmentId = department, PositionId = position, TasksJson = "[]", IsActive = true };
        var offboarding = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Off", TemplateType = "offboarding", LegalEntityId = legalEntity, TasksJson = "[]", IsActive = true };
        db.ChecklistTemplates.AddRange(company, dept, pos, offboarding);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var repository = new EfChecklistTemplateRepository(db);

        var matches = await repository.ListOnboardingMatchesAsync(tenant, legalEntity, department, position);

        matches.Select(m => m.Template.Id).Should().Equal(pos.Id, dept.Id, company.Id);
        matches.Select(m => m.MatchLevel).Should().Equal("position", "department", "company");
    }

    [Fact]
    public async Task InstantiateAsync_EditedJson_UsesAbsoluteDueDatesAndConcreteAssignedToId()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var template = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenant, Name = "Starter", TemplateType = "onboarding", TasksJson = "[]" };
        var json = $"[{{\"title\":\"Second\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{user}\",\"dueDate\":\"2026-09-01\",\"sequence\":2,\"isRequired\":true}},{{\"title\":\"First\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{user}\",\"dueDate\":\"2026-08-01\",\"sequence\":1,\"isRequired\":false}}]";
        var repository = new EfEmployeeChecklistTaskRepository(db);
        var tasks = await repository.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), json, new DateOnly(2026, 1, 1));
        tasks.Select(x => x.Sequence).Should().Equal(2, 1);
        tasks.Should().OnlyContain(x => x.TenantId == tenant && x.TemplateId == template.Id && x.LifecycleType == "onboarding");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), "{}", new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task InstantiateAsync_TemplateJsonWithoutEdits_ResolvesEmployeeOwnerAndOffsetsDueDates()
    {
        await using var db = BuildDb();
        var tenant = Guid.NewGuid(); var newHireUserId = Guid.NewGuid();
        var template = new ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenant, Name = "Starter", TemplateType = "onboarding", IsActive = true,
            TasksJson = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":2,\"isRequired\":true}]",
        };
        var repository = new EfEmployeeChecklistTaskRepository(db);

        var tasks = await repository.InstantiateAsync(template, Guid.NewGuid(), newHireUserId, editedTasksJson: null, new DateOnly(2026, 5, 1));

        tasks.Should().ContainSingle();
        tasks[0].AssignedToId.Should().Be(newHireUserId);
        tasks[0].DueDate.Should().Be(new DateOnly(2026, 5, 3));
    }

    [Fact]
    public async Task InstantiateAsync_NeverMutatesTheSourceTemplatesTasksJson()
    {
        await using var db = BuildDb();
        const string originalTasksJson = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":2,\"isRequired\":true}]";
        var template = new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Starter", TemplateType = "onboarding", TasksJson = originalTasksJson, IsActive = true };
        var repository = new EfEmployeeChecklistTaskRepository(db);

        await repository.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), editedTasksJson: null, new DateOnly(2026, 1, 1));
        await repository.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), editedTasksJson: "[{\"title\":\"Different task for this hire\",\"ownerType\":\"custom_user\",\"assignedToId\":\"" + Guid.NewGuid() + "\",\"dueDate\":\"2026-02-01\",\"isRequired\":false}]", new DateOnly(2026, 1, 1));

        template.TasksJson.Should().Be(originalTasksJson);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>(); var currentUser = new Mock<ICurrentUser>(); var publisher = new Mock<IPublisher>(); var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(options, new AuditableEntityInterceptor(currentUser.Object, clock.Object), new SoftDeleteInterceptor(clock.Object), new DomainEventDispatchInterceptor(publisher.Object), tenant.Object);
    }
}
