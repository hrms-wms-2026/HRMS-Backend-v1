# Work Management — Objective Member Management (Invite/Accept) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the direct-add Objective member model with an invite/accept model, add a members-list endpoint, and add a no-Reporting-Manager fallback branch to Transfer, without touching the existing Reporting-Manager approval system for Transfer-with-RM or Edit.

**Architecture:** Reuses the `project_member_invitations` table (scaffolded in the foundation migration, never wired to any API) by adding an `invite_type` column and a leader-uniqueness constraint. New CQRS commands/queries follow this codebase's existing MediatR + `Result<T>` pattern exactly, modeled on the sibling `ObjectiveChangeRequests` slice (Approve/Reject) already in the repo.

**Tech Stack:** .NET (ONEVO.Domain/Application/Infrastructure/Api), EF Core + PostgreSQL, MediatR, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-14-work-management-objective-member-management-design.md`

## Global Constraints

- **Scope guardrail:** touch only `src/ONEVO.*/Features/WorkManagement/**`, `src/ONEVO.Api/Controllers/Tenant/WorkManagement/**`, `src/ONEVO.Api/Contracts/WorkManagement/**`, `src/ONEVO.Infrastructure/Migrations/**`, `src/ONEVO.Infrastructure/Persistence/{Configurations,Repositories}/WorkManagement/**`, `tests/ONEVO.Tests.Unit/Features/WorkManagement/**`, and `docs/postman-request/Work Management/**`. Do not touch Core HR, Org Structure, or any other module.
- **Commit after every task**, never push (per user 2026-08-14).
- Every new/modified handler follows the existing `ICurrentUser` → tenant/auth guard → entity lookup → authorization → validation → mutate → `SaveChangesAsync` shape already used by every sibling handler in this folder.
- `invite_type` values are the string constants `'member'` / `'leader'` (`ProjectInvitationTypes.Member` / `.Leader`), never raw string literals in application code.

---

## Task 1: Schema — `invite_type` column + leader-uniqueness constraint

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberInvitationConfiguration.cs`
- Create: new EF migration (generated, not hand-written)

**Interfaces:**
- Produces: `ProjectInvitationTypes.Member` / `ProjectInvitationTypes.Leader` (string constants), `ProjectMemberInvitation.InviteType` (string property) — every later task reads/writes these.

- [ ] **Step 1: Add `InviteType` to the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

public static class ProjectInvitationStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public static class ProjectInvitationTypes
{
    public const string Member = "member";
    public const string Leader = "leader";
}

public class ProjectMemberInvitation : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitedEmployeeId { get; set; }
    public string InviteType { get; set; } = ProjectInvitationTypes.Member;
    public string Status { get; set; } = ProjectInvitationStatuses.Pending;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

- [ ] **Step 2: Configure the new column and the leader-uniqueness index**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberInvitationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberInvitationConfiguration : IEntityTypeConfiguration<ProjectMemberInvitation>
{
    public void Configure(EntityTypeBuilder<ProjectMemberInvitation> builder)
    {
        builder.ToTable("project_member_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();
        builder.Property(i => i.InviteType).HasMaxLength(20).IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.InvitedUserId, i.Status })
            .HasDatabaseName("ix_project_member_invitations_tenant_invited_user_status");
        builder.HasIndex(i => new { i.TenantId, i.ProjectId, i.ObjectiveId, i.InvitedUserId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_project_member_invitations_one_pending");
        // At most one pending leader-designate per objective at a time — enforces
        // "creator/current head stays owner until accepted" at the DB level.
        builder.HasIndex(i => new { i.TenantId, i.ObjectiveId })
            .IsUnique()
            .HasFilter("status = 'pending' AND invite_type = 'leader'")
            .HasDatabaseName("ix_project_member_invitations_one_pending_leader");

        builder.HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(i => i.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Generate the migration**

Run:
```bash
dotnet ef migrations add AddProjectMemberInvitationTypeAndLeaderUniqueness --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: a new `src/ONEVO.Infrastructure/Migrations/{timestamp}_AddProjectMemberInvitationTypeAndLeaderUniqueness.cs` + `.Designer.cs`, and `ApplicationDbContextModelSnapshot.cs` updated. Open the generated `Up()` and confirm it contains exactly: `AddColumn<string>("invite_type", "project_member_invitations", ..., defaultValue: "member")` and `CreateIndex(name: "ix_project_member_invitations_one_pending_leader", ...)`. If EF also tries to touch unrelated tables, stop and investigate — the model snapshot may be out of sync with a migration from another in-flight branch; do not force it through.

- [ ] **Step 4: Apply and verify against a real database**

Run:
```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Then verify the constraint actually exists (matching this repo's established verification standard — passing the migration isn't enough, confirm the live index):
```sql
SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'project_member_invitations';
```
Expected: `ix_project_member_invitations_one_pending_leader` present with `WHERE ((status)::text = 'pending'::text) AND ((invite_type)::text = 'leader'::text)`.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberInvitationConfiguration.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(work): add invite_type to project_member_invitations + leader-uniqueness constraint"
```

---

## Task 2: Repository — `IProjectMemberInvitationRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/RepositoryInterfaces/IProjectMemberInvitationRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberInvitationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfProjectMemberInvitationRepositoryTests.cs` (skip — no existing `Ef*RepositoryTests` file exists for any sibling repository in this codebase; repository correctness is exercised indirectly through the handler tests in Tasks 4–10, matching the existing pattern where `EfObjectiveRepository` also has no dedicated test file)

**Interfaces:**
- Produces: `IProjectMemberInvitationRepository` with the methods below — every later task's handler depends on this.

- [ ] **Step 1: Define the repository interface**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/RepositoryInterfaces/IProjectMemberInvitationRepository.cs
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;

public interface IProjectMemberInvitationRepository
{
    Task AddAsync(ProjectMemberInvitation invitation, CancellationToken ct = default);

    Task<ProjectMemberInvitation?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Tracked variant of <see cref="GetByIdForTenantAsync"/> for accept/reject mutation.</summary>
    Task<ProjectMemberInvitation?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>The single pending invitation for this exact (objective, user) pair, if any — used by Add Member's duplicate check and Remove Member's cancel branch.</summary>
    Task<ProjectMemberInvitation?> GetPendingForObjectiveAndUserAsync(Guid tenantId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>Tracked variant of <see cref="GetPendingForObjectiveAndUserAsync"/>, for Remove Member's cancel mutation.</summary>
    Task<ProjectMemberInvitation?> GetTrackedPendingForObjectiveAndUserAsync(Guid tenantId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>Every pending invitation for this objective — the "Request pending" rows merged into Get Objective Members.</summary>
    Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>Every pending invitation addressed to this user, across all objectives — backs My Objective Invitations.</summary>
    Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    void Update(ProjectMemberInvitation invitation);
}
```

- [ ] **Step 2: Implement it against `ApplicationDbContext`**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberInvitationRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberInvitationRepository : IProjectMemberInvitationRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberInvitationRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMemberInvitation invitation, CancellationToken ct = default)
    {
        await _db.Set<ProjectMemberInvitation>().AddAsync(invitation, ct);
    }

    public async Task<ProjectMemberInvitation?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
    }

    public async Task<ProjectMemberInvitation?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
    }

    public async Task<ProjectMemberInvitation?> GetPendingForObjectiveAndUserAsync(Guid tenantId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId
                && i.InvitedUserId == userId && i.Status == ProjectInvitationStatuses.Pending, ct);
    }

    public async Task<ProjectMemberInvitation?> GetTrackedPendingForObjectiveAndUserAsync(Guid tenantId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId
                && i.InvitedUserId == userId && i.Status == ProjectInvitationStatuses.Pending, ct);
    }

    public async Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ObjectiveId == objectiveId && i.Status == ProjectInvitationStatuses.Pending)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMemberInvitation>> ListPendingForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMemberInvitation>()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.InvitedUserId == userId && i.Status == ProjectInvitationStatuses.Pending)
            .ToListAsync(ct);
    }

    public void Update(ProjectMemberInvitation invitation)
    {
        _db.Set<ProjectMemberInvitation>().Update(invitation);
    }
}
```

- [ ] **Step 3: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, alongside the existing block (around the line registering `IObjectiveChangeRequestRepository`):

```csharp
services.AddScoped<EfProjectMemberInvitationRepository>();
services.AddScoped<IProjectMemberInvitationRepository>(sp => sp.GetRequiredService<EfProjectMemberInvitationRepository>());
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: build succeeds (0 errors). This task has no new business logic to unit-test in isolation — correctness is exercised through the handler tests in later tasks, matching this repo's existing convention of not unit-testing `Ef*Repository` classes directly.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/ src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberInvitationRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(work): add IProjectMemberInvitationRepository"
```

---

## Task 3: Response DTOs + mapper for invitations

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/DTOs/Responses/ProjectMemberInvitationResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Mappers/ProjectMemberInvitationMapper.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/ProjectInvitations/ProjectMemberInvitationViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/ProjectInvitations/ProjectMemberInvitationViewModelMapper.cs`

**Interfaces:**
- Produces: `ProjectMemberInvitationResponse` (application-layer DTO), `ProjectMemberInvitationMapper.ToResponse(ProjectMemberInvitation)`, `ProjectMemberInvitationViewModel` (API-layer, camelCase JSON shape), `.ToViewModel()` extension — every later task's endpoint returns these.

- [ ] **Step 1: Application-layer response record**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/DTOs/Responses/ProjectMemberInvitationResponse.cs
namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

public sealed record ProjectMemberInvitationResponse(
    Guid Id, Guid ProjectId, Guid ObjectiveId, Guid InvitedUserId, string InviteType,
    string Status, Guid InvitedById, DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt);
```

- [ ] **Step 2: Mapper**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Mappers/ProjectMemberInvitationMapper.cs
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;

public static class ProjectMemberInvitationMapper
{
    public static ProjectMemberInvitationResponse ToResponse(ProjectMemberInvitation invitation) => new(
        invitation.Id, invitation.ProjectId, invitation.ObjectiveId, invitation.InvitedUserId, invitation.InviteType,
        invitation.Status, invitation.InvitedById, invitation.DecidedAt, invitation.CreatedAt);
}
```

- [ ] **Step 3: API-layer view model + mapper**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/ProjectInvitations/ProjectMemberInvitationViewModel.cs
namespace ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

public class ProjectMemberInvitationViewModel
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedUserId { get; set; }
    public string InviteType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/ProjectInvitations/ProjectMemberInvitationViewModelMapper.cs
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

public static class ProjectMemberInvitationViewModelMapper
{
    public static ProjectMemberInvitationViewModel ToViewModel(this ProjectMemberInvitationResponse response) => new()
    {
        Id = response.Id, ProjectId = response.ProjectId, ObjectiveId = response.ObjectiveId,
        InvitedUserId = response.InvitedUserId, InviteType = response.InviteType, Status = response.Status,
        InvitedById = response.InvitedById, DecidedAt = response.DecidedAt, CreatedAt = response.CreatedAt
    };
}
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build src/ONEVO.Api` — expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/DTOs/ src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Mappers/ src/ONEVO.Api/Contracts/WorkManagement/ProjectInvitations/
git commit -m "feat(work): add ProjectMemberInvitation response DTOs and mappers"
```

---

## Task 4: Add Objective Member — invite instead of direct add, keyed by `employeeId`

**Amendment (2026-08-14):** the frontend's only people-search source returns Employee ids, not `userId` — see the spec's own amendment. This task's request/command therefore take `EmployeeId`, and the handler resolves the linked `Employee.UserId` internally via a new coordinator method before writing to any `userId`-typed field. `ProjectMemberInvitation` already stores both `InvitedUserId` and `InvitedEmployeeId`, so no entity change.

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/AddObjectiveMemberOutcomeResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AddMember` action only)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (Task 3).
- Produces: `IMilestoneMembershipCoordinator.GetActiveByEmployeeIdAsync(tenantId, employeeId)` and `.HasActiveMembershipAsync(...)` — both reused by Task 10 and Task 11. `AddObjectiveMemberOutcomeResponse(bool AlreadyMember, ProjectMemberInvitationResponse? Invitation)`.

- [ ] **Step 1: Write the failing tests**

Replace the body of `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddObjectiveMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid(); // the userId linked to MemberEmployeeId, resolved server-side
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(bool isActive = true, bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadId, IsActive = isActive, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AddObjectiveMemberCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, Employee? assignee = null, Guid? callerId = null, bool explicitNullAssignee = false,
        bool alreadyActiveMember = false, ProjectMemberInvitation? existingPendingInvite = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var mockAssignee = explicitNullAssignee ? null
            : assignee ?? new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId, EmploymentStatusId = EmploymentStatusIds.Active };
        membership.Setup(x => x.GetActiveByEmployeeIdAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(mockAssignee);
        membership.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, ObjectiveId, MemberUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyActiveMember);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetPendingForObjectiveAndUserAsync(TenantId, ObjectiveId, MemberUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPendingInvite);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddObjectiveMemberCommandHandler(currentUser.Object, objectives.Object, membership.Object, invitations.Object, unitOfWork.Object);
        return (handler, invitations, membership);
    }

    [Fact]
    public async Task Handle_NewInvite_CreatesPendingMemberInvitationAndReturns202Shape()
    {
        var (handler, invitations, _) = BuildHandler(SubObjective());

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Equal(ProjectInvitationTypes.Member, result.Value.Invitation!.InviteType);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedUserId == MemberUserId && i.InvitedEmployeeId == MemberEmployeeId
            && i.InviteType == ProjectInvitationTypes.Member && i.Status == ProjectInvitationStatuses.Pending), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_NoOpReturnsAlreadyMemberTrue()
    {
        var (handler, invitations, _) = BuildHandler(SubObjective(), alreadyActiveMember: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyPendingInvite_ReturnsConflict()
    {
        var existing = new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedUserId = MemberUserId, InvitedEmployeeId = MemberEmployeeId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending };
        var (handler, _, _) = BuildHandler(SubObjective(), existingPendingInvite: existing);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), explicitNullAssignee: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

Note: this test file needs `IMilestoneMembershipCoordinator.GetActiveByEmployeeIdAsync` and `.HasActiveMembershipAsync` — neither exists on the interface yet. Add them in Step 3 below.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests"`
Expected: FAIL to compile — `AddObjectiveMemberCommand`/`Handler` don't have the new shape yet, `GetActiveByEmployeeIdAsync`/`HasActiveMembershipAsync` don't exist on the coordinator interface.

- [ ] **Step 3: Add `GetActiveByEmployeeIdAsync` and `HasActiveMembershipAsync` to the membership coordinator**

First check whether `IEmployeeRepository` (consumed by `MilestoneMembershipCoordinator` today only via `GetByUserIdAsync`) already has an id-keyed lookup — open `src/ONEVO.Application/Features/WorkManagement/../CoreHr` (wherever `IEmployeeRepository` is declared; find it via a repo-wide search for `interface IEmployeeRepository`) before adding a new repository method, so this doesn't duplicate one that already exists under a different name.

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs
// Add these two methods to the existing interface:
    /// <summary>Null if there is no active Employee record with this id in this tenant. Used when the caller only has an employeeId (e.g. from a people-search UI), not a userId.</summary>
    Task<Employee?> GetActiveByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>True if the user has an active membership row scoped to exactly this objective.</summary>
    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs
// Add these two methods to the existing class. GetActiveByEmployeeIdAsync uses the repository
// method found in this step's search above (named GetByIdAsync or GetByIdForTenantAsync,
// whichever IEmployeeRepository actually exposes) - if none exists, add one following that
// interface's own existing GetByUserIdAsync as the pattern to copy, not a new invention:
    public async Task<Employee?> GetActiveByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct); // confirm exact method name per the search above
        return employee is not null && employee.EmploymentStatusId == EmploymentStatusIds.Active ? employee : null;
    }

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, userId, ct);
        return existing?.IsActive == true;
    }
```

- [ ] **Step 4: Update the request contract, response DTO, command, and handler**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberRequest.cs
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class AddObjectiveMemberRequest
{
    public Guid EmployeeId { get; set; }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/AddObjectiveMemberOutcomeResponse.cs
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record AddObjectiveMemberOutcomeResponse(bool AlreadyMember, ProjectMemberInvitationResponse? Invitation);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public sealed record AddObjectiveMemberCommand(Guid ObjectiveId, Guid EmployeeId) : IRequest<Result<AddObjectiveMemberOutcomeResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result<AddObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AddObjectiveMemberOutcomeResponse>> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<AddObjectiveMemberOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("Cannot add members to an achieved milestone.");

        if (objective.OwnerId != userId)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head can add members.");

        var assignee = await _membership.GetActiveByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("The member must be an active employee in this tenant.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, assignee.UserId, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Success(new AddObjectiveMemberOutcomeResponse(AlreadyMember: true, Invitation: null));

        if (await _invitations.GetPendingForObjectiveAndUserAsync(tenantId, objective.Id, assignee.UserId, ct) is not null)
            return Result<AddObjectiveMemberOutcomeResponse>.Conflict("An invitation is already pending for this user on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = objective.ProjectId,
            ObjectiveId = objective.Id,
            InvitedUserId = assignee.UserId,
            InvitedEmployeeId = assignee.Id,
            InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = userId,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _invitations.AddAsync(invitation, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<AddObjectiveMemberOutcomeResponse>.Success(
            new AddObjectiveMemberOutcomeResponse(AlreadyMember: false, ProjectMemberInvitationMapper.ToResponse(invitation)));
    }
}
```

Note: `Employee.UserId` must exist as a property on the `Employee` entity for `assignee.UserId` above to compile — it's already implied by `MilestoneMembershipCoordinator.GetActiveAssigneeAsync`'s existing use of `employee.EmploymentStatusId` on the same entity family and by the `employees.user_id` column documented in `phase1-table-inventory.md`; confirm the exact casing/property name against `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs` before writing this step for real, don't assume.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests"`
Expected: all 7 tests PASS.

- [ ] **Step 6: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing AddMember action:

    /// <summary>Invites an employee to this milestone. Head-only. Immediate no-op (204) if already an active member; otherwise creates a pending invitation (202) the invited user must accept.</summary>
    [HttpPost("{id:guid}/members")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddObjectiveMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddObjectiveMemberCommand(id, request.EmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.AlreadyMember
            ? NoContent()
            : StatusCode(202, result.Value.Invitation!.ToViewModel());
    }
```
Add `using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;` to the controller's usings.

- [ ] **Step 7: Build the whole solution and run the full Objectives test class once more**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests"`
Expected: build succeeds, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberRequest.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): Add Objective Member now creates a pending invitation, keyed by employeeId, instead of adding directly"
```

---

## Task 5: Remove Objective Member — extend to cancel a pending invite

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.GetTrackedPendingForObjectiveAndUserAsync` / `.Update` (Task 2).

- [ ] **Step 1: Add the failing test**

Add this test to the existing `RemoveObjectiveMemberCommandHandlerTests.cs` (keep all existing tests as-is; only add this one plus the constructor wiring for the new dependency — update `BuildHandler` to accept and pass a `Mock<IProjectMemberInvitationRepository>`, defaulting `GetTrackedPendingForObjectiveAndUserAsync` to return `null` unless a test overrides it):

```csharp
    [Fact]
    public async Task Handle_UserHasNoActiveMembershipButHasPendingInvite_CancelsInvitation()
    {
        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedUserId = MemberUserId,
            InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending
        };
        var (handler, invitations) = BuildHandler(SubObjective(), memberHasNoActiveRow: true, pendingInvite: invitation);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectInvitationStatuses.Cancelled, invitation.Status);
        invitations.Verify(x => x.Update(invitation), Times.Once);
    }

    [Fact]
    public async Task Handle_UserHasNeitherActiveMembershipNorPendingInvite_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(SubObjective(), memberHasNoActiveRow: true, pendingInvite: null);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
```

Add the necessary usings (`ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces`, `ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities`) and extend `BuildHandler`'s signature with `bool memberHasNoActiveRow = false, ProjectMemberInvitation? pendingInvite = null` — when `memberHasNoActiveRow` is true, make the mocked `IMilestoneMembershipCoordinator.DeactivateMembershipAsync`'s underlying active-row check report "nothing to deactivate" by having the handler's new existence check (Step 3 below) return false; wire `invitations.Setup(x => x.GetTrackedPendingForObjectiveAndUserAsync(...)).ReturnsAsync(pendingInvite)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RemoveObjectiveMemberCommandHandlerTests"`
Expected: FAIL to compile — handler constructor doesn't accept `IProjectMemberInvitationRepository` yet, and there's no way to signal "member has no active row."

- [ ] **Step 3: Add `HasActiveMembershipAsync` usage and the cancel branch to the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public class RemoveObjectiveMemberCommandHandler : IRequestHandler<RemoveObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveObjectiveMemberCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot remove members from an achieved milestone.");

        if (objective.OwnerId != userId)
            return Result.Forbidden("Only this milestone's head can remove members.");

        if (request.UserId == objective.OwnerId)
            return Result.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.UserId, ct))
        {
            await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.UserId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var pendingInvite = await _invitations.GetTrackedPendingForObjectiveAndUserAsync(tenantId, objective.Id, request.UserId, ct);
        if (pendingInvite is null)
            return Result.NotFound("This user has no active membership or pending invitation on this milestone.");

        pendingInvite.Status = ProjectInvitationStatuses.Cancelled;
        pendingInvite.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(pendingInvite);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RemoveObjectiveMemberCommandHandlerTests"`
Expected: all tests PASS (existing ones plus the two new ones).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/ tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): Remove Objective Member also cancels a pending invitation"
```

---

## Task 6: Get Objective Members — new merged query

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveMemberListResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveMemberItemResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveMemberListViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveMembersQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberRepository.ListForUserInProjectAsync`... actually needs a per-objective real-members list, which `IProjectMemberRepository` doesn't expose yet — add `ListActiveForObjectiveAsync(tenantId, objectiveId)`. `IProjectMemberInvitationRepository.ListPendingForObjectiveAsync` (Task 2).
- Produces: `ObjectiveMemberListResponse(IReadOnlyList<ObjectiveMemberItemResponse> Items)`, `ObjectiveMemberItemResponse(Guid UserId, bool Pending, bool IsHead, string? InviteType, Guid? InvitationId, DateTimeOffset SinceOrInvitedAt)`.

- [ ] **Step 1: Add `ListActiveForObjectiveAsync` to `IProjectMemberRepository`**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs
// Add to the existing interface:
    /// <summary>Every active project_members row scoped to this exact objective.</summary>
    Task<IReadOnlyList<ProjectMember>> ListActiveForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);
```

Implement it in `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs` (find this file first — it implements `IProjectMemberRepository`, follow its existing method style exactly):

```csharp
    public async Task<IReadOnlyList<ProjectMember>> ListActiveForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    {
        return await _db.Set<ProjectMember>()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ObjectiveId == objectiveId && m.IsActive)
            .ToListAsync(ct);
    }
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveMembersQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveMembersQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();
    private static readonly Guid InvitedUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private GetObjectiveMembersQueryHandler BuildHandler(Objective? objective, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = HeadId, IsActive = true, JoinedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = MemberUserId, IsActive = true, JoinedAt = DateTimeOffset.UtcNow }
            });
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, callerId ?? HeadId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, InvitedUserId = InvitedUserId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow }
            });

        return new GetObjectiveMembersQueryHandler(currentUser.Object, objectives.Object, members.Object, invitations.Object);
    }

    [Fact]
    public async Task Handle_ReturnsRealMembersAndPendingInvitationsMerged()
    {
        var handler = BuildHandler(SubObjective());

        var result = await handler.Handle(new GetObjectiveMembersQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.UserId == HeadId && i.IsHead && !i.Pending);
        Assert.Contains(result.Value.Items, i => i.UserId == MemberUserId && !i.IsHead && !i.Pending);
        Assert.Contains(result.Value.Items, i => i.UserId == InvitedUserId && i.Pending && i.InviteType == ProjectInvitationTypes.Member);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var handler = BuildHandler(null);

        var result = await handler.Handle(new GetObjectiveMembersQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveMembersQueryHandlerTests"`
Expected: FAIL to compile — `GetObjectiveMembersQuery`/`Handler` don't exist yet.

- [ ] **Step 4: Implement the response DTOs, query, and handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveMemberItemResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveMemberItemResponse(
    Guid UserId, bool IsHead, bool Pending, string? InviteType, Guid? InvitationId, DateTimeOffset SinceOrInvitedAt);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveMemberListResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveMemberListResponse(IReadOnlyList<ObjectiveMemberItemResponse> Items);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;

public sealed record GetObjectiveMembersQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveMemberListResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;

public class GetObjectiveMembersQueryHandler : IRequestHandler<GetObjectiveMembersQuery, Result<ObjectiveMemberListResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IPermissionResolver _permissionResolver;

    public GetObjectiveMembersQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IProjectMemberRepository members,
        IProjectMemberInvitationRepository invitations, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _invitations = invitations;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveMemberListResponse>> Handle(GetObjectiveMembersQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveMemberListResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveMemberListResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveMemberListResponse>.NotFound("Objective not found.");

        // Same visibility rule as GetObjectiveByIdQueryHandler: projects:read/* OR active
        // membership on this objective or an ancestor - copied from that handler verbatim.
        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, userId, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveMemberListResponse>.Forbidden("You do not have access to this milestone's members.");
        }

        var activeMembers = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, ct);
        var pendingInvites = await _invitations.ListPendingForObjectiveAsync(tenantId, objective.Id, ct);

        var items = new List<ObjectiveMemberItemResponse>();
        items.AddRange(activeMembers.Select(m => new ObjectiveMemberItemResponse(
            m.UserId, IsHead: m.UserId == objective.OwnerId, Pending: false, InviteType: null, InvitationId: null, SinceOrInvitedAt: m.JoinedAt)));
        items.AddRange(pendingInvites.Select(i => new ObjectiveMemberItemResponse(
            i.InvitedUserId, IsHead: false, Pending: true, InviteType: i.InviteType, InvitationId: i.Id, SinceOrInvitedAt: i.CreatedAt)));

        return Result<ObjectiveMemberListResponse>.Success(new ObjectiveMemberListResponse(items));
    }
}
```

The test file's `BuildHandler` (Step 2) needs a `Mock<IPermissionResolver>` added, wired the same way `GetObjectiveByIdQueryHandlerTests` wires it — `ResolveAsync` returning a permission set that includes `"projects:read"` for the default success-path tests (so the membership-walk branch is skipped by default), with a separate test for the non-read-all, non-Head, non-member 403 path returning an empty permission set and `HasActiveMembershipForAnyObjectiveAsync` returning `false`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveMembersQueryHandlerTests"`
Expected: all tests PASS, including the added `Handle_CallerNotHeadNotMemberNoReadAll_ReturnsForbidden` case.

- [ ] **Step 6: API view model + controller endpoint**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveMemberListViewModel.cs
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class ObjectiveMemberItemViewModel
{
    public Guid UserId { get; set; }
    public bool IsHead { get; set; }
    public bool Pending { get; set; }
    public string? InviteType { get; set; }
    public Guid? InvitationId { get; set; }
    public DateTimeOffset SinceOrInvitedAt { get; set; }
}

public class ObjectiveMemberListViewModel
{
    public List<ObjectiveMemberItemViewModel> Items { get; set; } = new();
}
```

Add to `ObjectiveViewModelMapper.cs`:
```csharp
    public static ObjectiveMemberListViewModel ToViewModel(this ObjectiveMemberListResponse response) => new()
    {
        Items = response.Items.Select(i => new ObjectiveMemberItemViewModel
        {
            UserId = i.UserId, IsHead = i.IsHead, Pending = i.Pending,
            InviteType = i.InviteType, InvitationId = i.InvitationId, SinceOrInvitedAt = i.SinceOrInvitedAt
        }).ToList()
    };
```

Add to `ObjectivesController.cs` (near `GetById`):
```csharp
    /// <summary>This milestone's real members merged with pending invitations. Same visibility rule as GetById.</summary>
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveMembersQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Build and re-run**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveMembersQueryHandlerTests"`
Expected: build succeeds, tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Application/Features/WorkManagement/ProjectMembers/ src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveMembersQueryHandlerTests.cs
git commit -m "feat(work): add Get Objective Members endpoint (real members + pending invitations)"
```

---

## Task 7: Accept Objective Invitation

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AcceptObjectiveInvitationCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.GetTrackedByIdForTenantAsync`/`.Update` (Task 2), `IObjectiveRepository`, `IMilestoneMembershipCoordinator` (existing, plus `GetTrackedActiveDirectChildrenAsync` for the leader-cascade case), `IPermissionAutoGrantService` (existing).
- Produces: nothing new consumed elsewhere — terminal action.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/AcceptObjectiveInvitationCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AcceptObjectiveInvitationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid InvitedUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();

    private static Objective SubObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private static ProjectMemberInvitation Invitation(string type, string status = "pending") => new()
    {
        Id = InvitationId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        InvitedUserId = InvitedUserId, InvitedEmployeeId = Guid.NewGuid(), InviteType = type, Status = status
    };

    private (AcceptObjectiveInvitationCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IObjectiveRepository> Objectives)
        BuildHandler(ProjectMemberInvitation? invitation, Objective? objective, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? InvitedUserId);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, InvitationId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, InvitedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = InvitedUserId, EmploymentStatusId = EmploymentStatusIds.Active });

        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));

        var handler = new AcceptObjectiveInvitationCommandHandler(currentUser.Object, invitations.Object, objectives.Object, membership.Object, autoGrant.Object, unitOfWork.Object);
        return (handler, invitations, membership, objectives);
    }

    [Fact]
    public async Task Handle_AcceptMemberInvite_UpsertsMembership()
    {
        var (handler, invitations, membership, _) = BuildHandler(Invitation(ProjectInvitationTypes.Member), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        invitations.Verify(x => x.Update(It.Is<ProjectMemberInvitation>(i => i.Status == ProjectInvitationStatuses.Accepted)), Times.Once);
    }

    [Fact]
    public async Task Handle_AcceptLeaderInvite_ReassignsHead()
    {
        var (handler, _, membership, objectives) = BuildHandler(Invitation(ProjectInvitationTypes.Leader), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == InvitedUserId)), Times.Once);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotInvitedUser_ReturnsForbidden()
    {
        var (handler, _, _, _) = BuildHandler(Invitation(ProjectInvitationTypes.Member), SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(Invitation(ProjectInvitationTypes.Member, status: "accepted"), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(null, SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var achieved = SubObjective();
        achieved.IsAchieved = true;
        var (handler, _, _, _) = BuildHandler(Invitation(ProjectInvitationTypes.Member), achieved);

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AcceptObjectiveInvitationCommandHandlerTests"`
Expected: FAIL to compile — the command/handler don't exist yet.

- [ ] **Step 3: Implement the command and handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;

public sealed record AcceptObjectiveInvitationCommand(Guid InvitationId) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;

public class AcceptObjectiveInvitationCommandHandler : IRequestHandler<AcceptObjectiveInvitationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;
    private readonly IUnitOfWork _unitOfWork;

    public AcceptObjectiveInvitationCommandHandler(
        ICurrentUser currentUser, IProjectMemberInvitationRepository invitations, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _invitations = invitations;
        _objectives = objectives;
        _membership = membership;
        _autoGrant = autoGrant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AcceptObjectiveInvitationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var invitation = await _invitations.GetTrackedByIdForTenantAsync(tenantId, request.InvitationId, ct);
        if (invitation is null)
            return Result.NotFound("Invitation not found.");

        if (invitation.InvitedUserId != userId)
            return Result.Forbidden("Only the invited user can accept this invitation.");

        if (invitation.Status != ProjectInvitationStatuses.Pending)
            return Result.Conflict("This invitation has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, invitation.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot accept an invitation on an achieved milestone.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            if (invitation.InviteType == ProjectInvitationTypes.Leader)
            {
                var oldHeadUserId = objective.OwnerId;
                objective.OwnerId = userId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = userId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, userId, invitation.InvitedEmployeeId, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadUserId, innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadUserId, objective.Id, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, userId, invitation.InvitedById, "projects:access", innerCt);
            }
            else
            {
                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, userId, invitation.InvitedEmployeeId, innerCt);
            }

            invitation.Status = ProjectInvitationStatuses.Accepted;
            invitation.DecidedAt = now;
            _invitations.Update(invitation);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AcceptObjectiveInvitationCommandHandlerTests"`
Expected: all 5 tests PASS.

- [ ] **Step 5: Add the controller endpoint**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Add near the AddMember/RemoveMember actions:

    /// <summary>Accepts a pending invitation. Caller must be the invited user. Member invites create membership; leader invites reassign the milestone's head.</summary>
    [HttpPost("~/api/v1/work/objectives/invitations/{invitationId:guid}/accept")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AcceptInvitation(Guid invitationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcceptObjectiveInvitationCommand(invitationId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
Add `using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;` to the usings.

- [ ] **Step 6: Build and re-run**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AcceptObjectiveInvitationCommandHandlerTests"`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AcceptObjectiveInvitationCommandHandlerTests.cs
git commit -m "feat(work): add Accept Objective Invitation endpoint"
```

---

## Task 8: Reject Objective Invitation

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/RejectObjectiveInvitationCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/RejectObjectiveInvitationCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveInvitationCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository` (Task 2) and `IObjectiveRepository` (existing, for the achieved-freeze check only — reject still has no membership/head side effects).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveInvitationCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RejectObjectiveInvitationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InvitedUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();

    private static ProjectMemberInvitation Invitation(string status = "pending") => new()
    {
        Id = InvitationId, TenantId = TenantId, ProjectId = Guid.NewGuid(), ObjectiveId = ObjectiveId,
        InvitedUserId = InvitedUserId, InvitedEmployeeId = Guid.NewGuid(), InviteType = ProjectInvitationTypes.Leader, Status = status
    };

    private static Objective TargetObjective(bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = Guid.NewGuid(), IsDefault = false, Title = "Sub",
        OwnerId = Guid.NewGuid(), IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private (RejectObjectiveInvitationCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations) BuildHandler(
        ProjectMemberInvitation? invitation, Objective? objective = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? InvitedUserId);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, InvitationId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective ?? TargetObjective());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectObjectiveInvitationCommandHandler(currentUser.Object, invitations.Object, objectives.Object, unitOfWork.Object);
        return (handler, invitations);
    }

    [Fact]
    public async Task Handle_RejectPendingInvite_MarksDeclined_NoObjectiveSideEffects()
    {
        var (handler, invitations) = BuildHandler(Invitation());

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.Update(It.Is<ProjectMemberInvitation>(i => i.Status == ProjectInvitationStatuses.Declined)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotInvitedUser_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Invitation(), callerId: OtherUserId);

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(Invitation(status: "accepted"));

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(Invitation(), objective: TargetObjective(isAchieved: true));

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RejectObjectiveInvitationCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the command and handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/RejectObjectiveInvitationCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;

public sealed record RejectObjectiveInvitationCommand(Guid InvitationId) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/RejectObjectiveInvitationCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;

public class RejectObjectiveInvitationCommandHandler : IRequestHandler<RejectObjectiveInvitationCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public RejectObjectiveInvitationCommandHandler(
        ICurrentUser currentUser, IProjectMemberInvitationRepository invitations, IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _invitations = invitations;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectObjectiveInvitationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var invitation = await _invitations.GetTrackedByIdForTenantAsync(tenantId, request.InvitationId, ct);
        if (invitation is null)
            return Result.NotFound("Invitation not found.");

        if (invitation.InvitedUserId != userId)
            return Result.Forbidden("Only the invited user can reject this invitation.");

        if (invitation.Status != ProjectInvitationStatuses.Pending)
            return Result.Conflict("This invitation has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, invitation.ObjectiveId, ct);
        if (objective is not null && objective.IsAchieved)
            return Result.Failure("Cannot reject an invitation on an achieved milestone.");

        invitation.Status = ProjectInvitationStatuses.Declined;
        invitation.DecidedAt = DateTimeOffset.UtcNow;
        _invitations.Update(invitation);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RejectObjectiveInvitationCommandHandlerTests"`

- [ ] **Step 5: Add the controller endpoint**

```csharp
    /// <summary>Rejects a pending invitation. Caller must be the invited user. No side effects - for a leader invite, the current head simply remains head.</summary>
    [HttpPost("~/api/v1/work/objectives/invitations/{invitationId:guid}/reject")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RejectInvitation(Guid invitationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectObjectiveInvitationCommand(invitationId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
Add `using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;` to the usings.

- [ ] **Step 6: Build and re-run, then commit**

```bash
dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~RejectObjectiveInvitationCommandHandlerTests"
git add src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveInvitationCommandHandlerTests.cs
git commit -m "feat(work): add Reject Objective Invitation endpoint"
```

---

## Task 9: My Objective Invitations

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/GetMyObjectiveInvitations/GetMyObjectiveInvitationsQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/GetMyObjectiveInvitations/GetMyObjectiveInvitationsQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyObjectiveInvitationsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.ListPendingForUserAsync` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (Task 3).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyObjectiveInvitationsQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyObjectiveInvitationsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsCallersPendingInvitations()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(CallerId);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForUserAsync(TenantId, CallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation> {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(), InvitedUserId = CallerId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetMyObjectiveInvitationsQueryHandler(currentUser.Object, invitations.Object);

        var result = await handler.Handle(new GetMyObjectiveInvitationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(CallerId, result.Value![0].InvitedUserId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyObjectiveInvitationsQueryHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the query and handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/GetMyObjectiveInvitations/GetMyObjectiveInvitationsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;

public sealed record GetMyObjectiveInvitationsQuery : IRequest<Result<IReadOnlyList<ProjectMemberInvitationResponse>>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/GetMyObjectiveInvitations/GetMyObjectiveInvitationsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;

public class GetMyObjectiveInvitationsQueryHandler : IRequestHandler<GetMyObjectiveInvitationsQuery, Result<IReadOnlyList<ProjectMemberInvitationResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberInvitationRepository _invitations;

    public GetMyObjectiveInvitationsQueryHandler(ICurrentUser currentUser, IProjectMemberInvitationRepository invitations)
    {
        _currentUser = currentUser;
        _invitations = invitations;
    }

    public async Task<Result<IReadOnlyList<ProjectMemberInvitationResponse>>> Handle(GetMyObjectiveInvitationsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Forbidden("Tenant context missing.");

        var pending = await _invitations.ListPendingForUserAsync(tenantId, userId, ct);

        return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Success(
            pending.Select(ProjectMemberInvitationMapper.ToResponse).ToList());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyObjectiveInvitationsQueryHandlerTests"`

- [ ] **Step 5: Add the controller endpoint**

```csharp
    /// <summary>The caller's own pending invitations across every objective they've been invited to.</summary>
    [HttpGet("~/api/v1/work/objectives/invitations/mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> MyInvitations(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyObjectiveInvitationsQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(i => i.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
Add `using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;` and `using ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;` to the usings (the latter may already be present from Task 4).

- [ ] **Step 6: Build, re-run, commit**

```bash
dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyObjectiveInvitationsQueryHandlerTests"
git add src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyObjectiveInvitationsQueryHandlerTests.cs
git commit -m "feat(work): add My Objective Invitations endpoint"
```

---

## Task 10: Transfer Objective Head — no-Reporting-Manager branch, keyed by `employeeId`

**Amendment (2026-08-14):** same reason as Task 4 — the new head is picked via the people-search UI, which only has an Employee id. `TransferObjectiveHeadRequest`/`Command` now take `NewHeadEmployeeId`; the handler resolves `Employee.UserId` **once**, immediately after the permission checks, and reuses that resolved `userId` in all three branches below — including the existing RM-routing branch, so `TransferObjectiveRequestPayload` (read by the pre-existing, unmodified `ApproveObjectiveChangeRequestCommandHandler`) keeps storing a `userId` exactly as it does today. That handler is not touched by this task.

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveChangeOutcomeResponse.cs` → replaced by a new `TransferOutcomeResponse` used only by Transfer (do not change the existing type — Delete/Edit/Achieve/Unachieve keep using `ObjectiveChangeOutcomeResponse` unmodified)
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/TransferOutcomeResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer` action only)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (Task 3), `IMilestoneMembershipCoordinator.GetActiveByEmployeeIdAsync` (Task 4).
- Produces: `TransferOutcomeResponse(bool Applied, ObjectiveChangeRequestResponse? PendingChangeRequest, ProjectMemberInvitationResponse? PendingInvitation)`.

- [ ] **Step 1: Add the failing test**

Add this test to the existing `TransferObjectiveHeadCommandHandlerTests.cs` (first read that file in full to see its current `BuildHandler` shape — it will need a new `Mock<IProjectMemberInvitationRepository>` parameter threaded through the constructor call, mirroring how Task 4/5 extended their sibling test files, and its existing `GetActiveAssigneeAsync` mock setup swapped for `GetActiveByEmployeeIdAsync`):

```csharp
    [Fact]
    public async Task Handle_NonCreatorCaller_ObjectiveHasNoReportingManager_CreatesLeaderInvitationInsteadOfChangeRequest()
    {
        var objective = SubObjective(); // set ReportingManagerId = null and CreatedById = some OtherUserId, not HeadId
        objective.ReportingManagerId = null;
        objective.CreatedById = Guid.NewGuid(); // caller (HeadId) did not create it
        var newHeadEmployeeId = Guid.NewGuid();
        var newHeadUserId = Guid.NewGuid();

        var (handler, invitations, changeRequests) = BuildHandler(objective, newHeadEmployeeId: newHeadEmployeeId, newHeadUserId: newHeadUserId);

        var result = await handler.Handle(new TransferObjectiveHeadCommand(ObjectiveId, newHeadEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingInvitation);
        Assert.Null(result.Value.PendingChangeRequest);
        Assert.Equal(HeadId, objective.OwnerId); // caller stays Head until accepted
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedUserId == newHeadUserId && i.InvitedEmployeeId == newHeadEmployeeId
            && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
        changeRequests.Verify(x => x.AddAsync(It.IsAny<ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Extend `BuildHandler` with optional `Guid? newHeadEmployeeId = null, Guid? newHeadUserId = null` parameters, defaulting both to freshly-generated guids when omitted, and wire `membership.Setup(x => x.GetActiveByEmployeeIdAsync(TenantId, newHeadEmployeeId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { Id = newHeadEmployeeId.Value, TenantId = TenantId, UserId = newHeadUserId.Value, EmploymentStatusId = EmploymentStatusIds.Active })` in place of the old `GetActiveAssigneeAsync` setup. Also construct and return a `Mock<IProjectMemberInvitationRepository>`, passed into the handler's constructor as its new dependency, and update every existing test's constructed `TransferObjectiveHeadCommand(ObjectiveId, someUserId)` call to pass an employeeId instead (renaming the local variable at each call site, e.g. `newHeadId` → `newHeadEmployeeId`, and wiring its matching `GetActiveByEmployeeIdAsync` mock), plus rename every existing assertion on `result.Value!.Applied`/`.PendingRequest` to `.PendingChangeRequest` (the property is renamed, not removed).

- [ ] **Step 2: Run tests to verify the new one fails and existing ones fail to compile**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TransferObjectiveHeadCommandHandlerTests"`
Expected: FAIL to compile — `TransferOutcomeResponse` doesn't exist yet, `PendingRequest` renamed, `GetActiveByEmployeeIdAsync` not yet mocked correctly against the old handler shape.

- [ ] **Step 3: Add the new response type**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/TransferOutcomeResponse.cs
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record TransferOutcomeResponse(
    bool Applied, ObjectiveChangeRequestResponse? PendingChangeRequest, ProjectMemberInvitationResponse? PendingInvitation);
```

- [ ] **Step 4: Update the request contract and command's return type**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class TransferObjectiveHeadRequest
{
    public Guid NewHeadEmployeeId { get; set; }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public sealed record TransferObjectiveHeadCommand(Guid ObjectiveId, Guid NewHeadEmployeeId) : IRequest<Result<TransferOutcomeResponse>>;
```

- [ ] **Step 5: Rewrite the handler — resolve the new head's `userId` once up front, reuse it in all three branches, wrap the immediate-apply return in the new shape, and insert the no-RM branch**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<TransferOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
    }

    public async Task<Result<TransferOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TransferOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<TransferOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TransferOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<TransferOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<TransferOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (objective.OwnerId != userId)
            return Result<TransferOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        // Resolved once, reused by every branch below - including the existing RM-routing branch,
        // so the change-request payload keeps storing a userId exactly as it always has.
        var newHeadAssignee = await _membership.GetActiveByEmployeeIdAsync(tenantId, request.NewHeadEmployeeId, ct);
        if (newHeadAssignee is null)
            return Result<TransferOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");
        var newHeadUserId = newHeadAssignee.UserId;

        if (objective.CreatedById == userId)
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadUserId = objective.OwnerId;

                objective.OwnerId = newHeadUserId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = newHeadUserId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, newHeadUserId, newHeadAssignee.Id, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadUserId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, newHeadUserId, userId, "projects:access", innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadUserId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<TransferOutcomeResponse>.Success(new TransferOutcomeResponse(Applied: true, PendingChangeRequest: null, PendingInvitation: null));
            }, ct);
        }

        // New branch (2026-08-14): no Reporting Manager to route an approval to — send a direct,
        // no-approval invitation to the proposed new head instead. Caller remains Head until accepted.
        if (objective.ReportingManagerId is null)
        {
            var invitation = new ProjectMemberInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = objective.ProjectId,
                ObjectiveId = objective.Id,
                InvitedUserId = newHeadUserId,
                InvitedEmployeeId = newHeadAssignee.Id,
                InviteType = ProjectInvitationTypes.Leader,
                Status = ProjectInvitationStatuses.Pending,
                InvitedById = userId,
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _invitations.AddAsync(invitation, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<TransferOutcomeResponse>.Success(
                new TransferOutcomeResponse(Applied: false, PendingChangeRequest: null, PendingInvitation: ProjectMemberInvitationMapper.ToResponse(invitation)));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<TransferOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new TransferObjectiveRequestPayload(newHeadUserId);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Transfer,
            RequestedById = userId,
            ReportingManagerId = objective.ReportingManagerId.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<TransferOutcomeResponse>.Success(
            new TransferOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest), PendingInvitation: null));
    }
}
```

Note the `objective.ReportingManagerId.Value` (no more `!`) on the last remaining branch — now safe both in fact and in the type checker, since the `is null` branch above already handles the null case explicitly instead of relying on an assumed invariant. `TransferObjectiveRequestPayload`'s own field name/shape is unchanged (still `NewHeadUserId`) — only the local variable feeding it here changed from a direct request field to a resolved one.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TransferObjectiveHeadCommandHandlerTests"`
Expected: all tests (existing, renamed, plus the new one) PASS.

- [ ] **Step 7: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing Transfer action:

    /// <summary>Reassigns a milestone's head (by employeeId, resolved to the linked user internally). If the objective has a Reporting Manager, applies immediately for the creator or routes to that Reporting Manager for approval otherwise (unchanged). If the objective has no Reporting Manager, skips approval entirely and sends a direct invitation to the proposed new head, who must accept it - the caller remains Head until then.</summary>
    [HttpPost("{id:guid}/transfer")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadEmployeeId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        if (result.Value!.Applied)
            return NoContent();

        return result.Value.PendingInvitation is not null
            ? Accepted(result.Value.PendingInvitation.ToViewModel())
            : Accepted(result.Value.PendingChangeRequest!.ToViewModel());
    }
```

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build src/ONEVO.Api`
Expected: 0 errors. Since `ObjectiveChangeOutcomeResponse` (unchanged) is still used by Delete/Edit/Achieve/Unachieve, confirm those four actions in the same controller file still compile untouched — this task only renamed Transfer's own response type, not the shared one. Also confirm `ApproveObjectiveChangeRequestCommandHandler` (untouched) still compiles — it deserializes `TransferObjectiveRequestPayload` exactly as before, unaffected by this task's `employeeId`-vs-`userId` change since that change never reached the payload's own shape.

- [ ] **Step 9: Full Objectives test class + solution-wide test run**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: every Work Management test passes (this catches any other place that referenced `TransferObjectiveHeadCommandHandler`'s old response shape that wasn't in the file list above).

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs
git commit -m "feat(work): Transfer sends a direct leader invitation when the objective has no Reporting Manager, keyed by employeeId"
```

---

## Task 11: Create Objective — creator always starts as owner; optional invitations, keyed by `employeeId`

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create` action only)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs` (find this file first — it exists per the sibling-tests pattern already established for every other handler in this folder)

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.AddAsync` (Task 2), `IMilestoneMembershipCoordinator.GetActiveByEmployeeIdAsync` (Task 4).
- Produces: nothing new — `CreateObjective`'s existing `HeadUserId` field is **repurposed and renamed** to `HeadEmployeeId`: it now means "invite this person as leader" instead of "immediately make this person the head," and (per the 2026-08-14 employeeId amendment) is keyed by Employee id like every other invite entry point in this plan.

**⚠️ Two behavior changes to flag explicitly:** (1) today, `HeadUserId` on Create **immediately** sets `objective.OwnerId` to that user, bypassing "creator becomes owner first" entirely — this contradicts the user's stated rule that the creator is always the starting owner. (2) The field is also renamed from a `userId` to an `employeeId` identifier. Call both out to the user when reporting this task's completion, not just in this plan file.

- [ ] **Step 1: Read the existing test file to see its current shape**

Open `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs` in full before writing new tests — it almost certainly has a test asserting the current "HeadUserId immediately sets OwnerId" behavior, which this task inverts. That test's name and assertion need to change, not just gain neighbors.

- [ ] **Step 2: Write/update the failing tests**

Add/replace tests in `CreateObjectiveCommandHandlerTests.cs` to cover:

```csharp
    [Fact]
    public async Task Handle_NoHeadEmployeeIdOrInvitations_CreatorIsOwnerImmediately_NoInvitationsCreated()
    {
        var (handler, invitations, _) = BuildHandler(ParentObjective());

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadEmployeeId: null, MemberInvitations: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CallerId, result.Value!.OwnerId); // creator is owner, always
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HeadEmployeeIdDifferentFromCreator_CreatorStillOwnerImmediately_LeaderInvitationCreated()
    {
        var proposedHeadEmployeeId = Guid.NewGuid();
        var proposedHeadUserId = Guid.NewGuid();
        var (handler, invitations, membership) = BuildHandler(ParentObjective());
        membership.Setup(x => x.GetActiveByEmployeeIdAsync(TenantId, proposedHeadEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = proposedHeadEmployeeId, TenantId = TenantId, UserId = proposedHeadUserId, EmploymentStatusId = EmploymentStatusIds.Active });

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadEmployeeId: proposedHeadEmployeeId, MemberInvitations: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CallerId, result.Value!.OwnerId); // NOT proposedHeadUserId - creator stays owner until accepted
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.InvitedUserId == proposedHeadUserId && i.InvitedEmployeeId == proposedHeadEmployeeId && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MemberInvitationsProvided_CreatesOnePendingInvitePerEntry()
    {
        var memberOneEmployeeId = Guid.NewGuid();
        var memberOneUserId = Guid.NewGuid();
        var memberTwoEmployeeId = Guid.NewGuid();
        var memberTwoUserId = Guid.NewGuid();
        var (handler, invitations, membership) = BuildHandler(ParentObjective());
        membership.Setup(x => x.GetActiveByEmployeeIdAsync(TenantId, memberOneEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = memberOneEmployeeId, TenantId = TenantId, UserId = memberOneUserId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.GetActiveByEmployeeIdAsync(TenantId, memberTwoEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = memberTwoEmployeeId, TenantId = TenantId, UserId = memberTwoUserId, EmploymentStatusId = EmploymentStatusIds.Active });

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadEmployeeId: null,
            MemberInvitations: new List<(Guid EmployeeId, string Type)> { (memberOneEmployeeId, "member"), (memberTwoEmployeeId, "member") }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedUserId == memberOneUserId && i.InvitedEmployeeId == memberOneEmployeeId && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedUserId == memberTwoUserId && i.InvitedEmployeeId == memberTwoEmployeeId && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Extend `BuildHandler` in that file to also construct a `Mock<IProjectMemberInvitationRepository>` and return the `Mock<IMilestoneMembershipCoordinator>` it already builds internally (it currently only returns the handler), so callers can add per-test `GetActiveByEmployeeIdAsync` setups as shown above — mirroring how Tasks 4/5/10 extended their own sibling test files. The existing membership-upsert assertions that assumed a non-creator `HeadUserId`/`HeadEmployeeId` was upserted directly must be removed — that upsert no longer happens for a non-creator head.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateObjectiveCommandHandlerTests"`
Expected: FAIL to compile — `CreateObjectiveCommand` doesn't have `HeadEmployeeId`/`MemberInvitations` yet, and the existing immediate-head-assignment test (if not yet updated per Step 1) will fail on the new expected behavior.

- [ ] **Step 4: Update the request contract, command, and handler**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class CreateObjectiveMemberInvitationRequest
{
    public Guid EmployeeId { get; set; }
    public string Type { get; set; } = "member"; // "member" | "leader"
}

public class CreateObjectiveRequest
{
    public Guid ParentObjectiveId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
    /// <summary>If set and different from the creator's own employee record, invites this person as leader (pending accept) - does not immediately assign headship. See TransferObjectiveHead's invite flow for the same acceptance mechanism.</summary>
    public Guid? HeadEmployeeId { get; set; }
    public List<CreateObjectiveMemberInvitationRequest>? MemberInvitations { get; set; }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public sealed record CreateObjectiveCommand(
    Guid ParentObjectiveId,
    string Title,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AllocatedHours,
    Guid? HeadEmployeeId,
    IReadOnlyList<(Guid EmployeeId, string Type)>? MemberInvitations
) : IRequest<Result<ObjectiveDetailResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IProjectMemberInvitationRepository invitations)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _invitations = invitations;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        if (parent.OwnerId != userId)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        // Creator-employee check happens regardless of HeadEmployeeId — the creator is always the
        // Objective's immediate owner and its first membership row. Resolved by userId (the
        // caller's own session identity, not a picker selection), unlike the two checks below.
        var creatorAssignee = await _membership.GetActiveAssigneeAsync(tenantId, userId, ct);
        if (creatorAssignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The creator must be an active employee in this tenant.");

        // A proposed non-creator leader must resolve to an active employee before anything is
        // created, same fail-fast-before-any-write shape as every other handler in this file.
        Domain.Features.CoreHr.Entities.Employee? proposedHeadAssignee = null;
        if (request.HeadEmployeeId.HasValue)
        {
            proposedHeadAssignee = await _membership.GetActiveByEmployeeIdAsync(tenantId, request.HeadEmployeeId.Value, ct);
            if (proposedHeadAssignee is null)
                return Result<ObjectiveDetailResponse>.Failure("The proposed head must be an active employee in this tenant.");
            if (proposedHeadAssignee.UserId == userId)
                proposedHeadAssignee = null; // proposed head IS the creator - no invitation needed, they're already owner
        }

        var resolvedMemberInvitees = new List<(Guid EmployeeId, Guid UserId, string Type)>();
        if (request.MemberInvitations is not null)
        {
            foreach (var invite in request.MemberInvitations)
            {
                var inviteeAssignee = await _membership.GetActiveByEmployeeIdAsync(tenantId, invite.EmployeeId, ct);
                if (inviteeAssignee is null)
                    return Result<ObjectiveDetailResponse>.Failure($"Invited member (employee {invite.EmployeeId}) must be an active employee in this tenant.");
                resolvedMemberInvitees.Add((invite.EmployeeId, inviteeAssignee.UserId, invite.Type));
            }
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            var objective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = parent.ProjectId,
                ParentObjectiveId = parent.Id,
                IsDefault = false,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                // Creator is always the starting owner (user rule, 2026-08-14) - HeadEmployeeId no
                // longer bypasses this; it only queues a leader invitation below.
                OwnerId = userId,
                ReportingManagerId = userId,
                IsActive = true,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Progress = 0m,
                AllocatedHours = request.AllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            await _objectives.AddAsync(objective, innerCt);
            await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, userId, creatorAssignee.Id, innerCt);

            if (proposedHeadAssignee is not null)
            {
                await _invitations.AddAsync(new ProjectMemberInvitation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                    InvitedUserId = proposedHeadAssignee.UserId, InvitedEmployeeId = proposedHeadAssignee.Id,
                    InviteType = ProjectInvitationTypes.Leader, Status = ProjectInvitationStatuses.Pending,
                    InvitedById = userId, CreatedById = userId, CreatedAt = now
                }, innerCt);
            }

            foreach (var invitee in resolvedMemberInvitees)
            {
                await _invitations.AddAsync(new ProjectMemberInvitation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                    InvitedUserId = invitee.UserId, InvitedEmployeeId = invitee.EmployeeId,
                    InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending,
                    InvitedById = userId, CreatedById = userId, CreatedAt = now
                }, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
}
```

Note: `IPermissionAutoGrantService` is dropped from this handler entirely (it was only ever invoked for the immediate head-assignment path, which no longer exists — a leader invite's accept step grants access instead, per Task 7). Do not leave the field/constructor parameter/using in place unused — an unused injected dependency is exactly the kind of leftover this codebase's architecture tests have caught before (see `ONEVO_Backend_Architecture_Document.md` §3.3.1 precedent). Re-check this handler's final form has no unused `using` or field before moving to Step 5. Also resolve the fully-qualified `Domain.Features.CoreHr.Entities.Employee` reference above to a proper `using` matching however `MilestoneMembershipCoordinator.cs` itself imports `Employee` (seen earlier in this plan as `ONEVO.Domain.Features.CoreHr.Entities`) — written fully-qualified here only to make the type unambiguous in this plan document, not as the literal final form.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateObjectiveCommandHandlerTests"`
Expected: all tests PASS, including every pre-existing test not touched by this task's new cases.

- [ ] **Step 6: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing Create action's command construction line:

        var command = new CreateObjectiveCommand(
            request.ParentObjectiveId, request.Title, request.Description,
            request.StartDate, request.EndDate, request.AllocatedHours, request.HeadEmployeeId,
            request.MemberInvitations?.Select(m => (m.EmployeeId, m.Type)).ToList());
```
(The rest of the `Create` action — the `result.IsSuccess ? StatusCode(201, ...) : Problem(...)` shape — is unchanged; `ObjectiveDetailResponse`'s own shape didn't change, only how `OwnerId` gets set.)

- [ ] **Step 7: Build the whole solution and run every Work Management test**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: 0 build errors, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs
git commit -m "feat(work): Create Objective - creator is always the starting owner; HeadEmployeeId now invites a leader instead of assigning immediately"
```

---

## Task 12: Postman docs

**Files:**
- Modify: `docs/postman-request/Work Management/Add Objective Member.md`
- Modify: `docs/postman-request/Work Management/Remove Objective Member.md`
- Modify: `docs/postman-request/Work Management/Transfer Objective Head.md`
- Modify: `docs/postman-request/Work Management/Create Objective.md`
- Create: `docs/postman-request/Work Management/Get Objective Members.md`
- Create: `docs/postman-request/Work Management/Accept Objective Invitation.md`
- Create: `docs/postman-request/Work Management/Reject Objective Invitation.md`
- Create: `docs/postman-request/Work Management/My Objective Invitations.md`

- [ ] **Step 1: Update the four existing docs**

For each of the four `Modify` files, rewrite the **Description**, **Response**, and **Errors** sections to match the actual new handler behavior implemented in Tasks 4/5/10/11 (do not restate old behavior) — follow this repo's existing template exactly, one section at a time, cross-checking each field/status code against the handler code just written, not against memory of what was planned. Keep the `## Source` section's controller/handler paths accurate (unchanged paths for Add/Remove/Transfer/Create, since no file was renamed).

- [ ] **Step 2: Write the four new docs**

Follow the exact template of the existing `Approve Objective Change Request.md` (for Accept/Reject Invitation — same shape, different table/entity) and `Get Objective.md` (for Get Objective Members — same auth/permission header style) as templates. Each new doc needs: method + path, Auth/Permission line, Description, Request/Response bodies matching the actual DTOs from Tasks 6/7/8/9, Errors table, and a `## Source` section with the real controller action name and handler path.

- [ ] **Step 3: Cross-check every doc against the actual running code**

For each of the 8 files, re-open the corresponding controller action and handler side-by-side with the doc and confirm every status code, field name, and permission claim in the doc is exactly what the code does — not what an earlier task's plan said it would do. This is the same standard this repo's own `PROCESS_RULES.md` rule 6 requires (docs and code change together, verified against the real thing).

- [ ] **Step 4: Commit**

```bash
git add "docs/postman-request/Work Management/"
git commit -m "docs: update Work Management Postman docs for the invite/accept member model"
```

---

# Phase 2: UserId → Employee-Based Identity (Full Work Management Rework)

**Added 2026-08-14, after Tasks 1–12 above were written.** During Task 4/10/11 self-review it emerged that the people-picker only has Employee ids (§ Task 4/10/11 amendments above resolved this at the API boundary only). The user then clarified the real requirement: one **User** can be linked to **multiple Employee records** (one per legal entity — a tenant can control several legal entities), so `UserId` can no longer serve as a person's identity within Work Management at all — only `EmployeeId` is unambiguous. This phase replaces `UserId` with `EmployeeId` as the ownership/membership identity **everywhere** in Work Management, superseding the boundary-only resolution added in Tasks 4/10/11 (those tasks' `GetActiveByEmployeeIdAsync` boundary hop becomes unnecessary — `employeeId` now flows straight through end to end. Tasks 19 and 22 below rewrite Tasks 10 and 4 in full).

**Scope boundary for this phase (read before starting any task below):**

- **IN scope** (business identity — who owns/leads/is-a-member-of a Project or Objective): `Project.LeadId`, `Objective.OwnerId`, `Objective.ReportingManagerId`, `ObjectiveChangeRequest.RequestedById`, `ObjectiveChangeRequest.ReportingManagerId`, `ObjectiveChangeRequest.DecidedById`, `ProjectMember.UserId` (dropped — `EmployeeId` already exists on the entity and is already populated everywhere it's constructed), `ProjectMemberInvitation.InvitedUserId` (dropped — same reasoning, `InvitedEmployeeId` already exists), every `IMilestoneMembershipCoordinator`/`IProjectMemberRepository` parameter that identifies a person, `ListProjectsQuery.TargetUserId`.
- **OUT of scope, deliberately untouched:** `BaseEntity.CreatedById` (system-wide audit convention inherited by every entity in every module, not a Work Management business field), `ReleaseCalendarEntry.RecipientUserId` and `AuditLog.UserId` (notification/audit delivery, a separate concern from ownership), `EntityAsset.CreatedById`/`CreatedByType`. If a future slice needs these to be Employee-based too, that's a separate decision — don't fold it into this phase.
- **Core HR's `employees.user_id` unique index stays as-is** (confirmed: `EmployeeConfiguration.cs:24`, `builder.HasIndex(e => e.UserId).IsUnique();`). Per the user's explicit decision, this phase does **not** touch Core HR to lift that constraint. Practical effect: today, resolving "the caller's EmployeeId" from their session `UserId` is still safe as a single unambiguous lookup (`ICallerIdentityResolver` below, Task 14) — there is exactly one Employee row per User right now. The moment Core HR lifts that constraint (out of this phase's scope, a teammate's future change), `ICallerIdentityResolver` becomes the **one place** that needs to grow a legal-entity-scoped disambiguation step instead of a plain lookup — every handler downstream is unaffected because they only ever consume the resolved `Guid employeeId`, never `UserId` directly.
- **API contract fields keep their existing JSON names** (`ownerId`, `reportingManagerId`, `requestedById`, `leadId`, etc.) — only the *meaning* of the Guid they carry changes, from `users.id` to `employees.id`. This avoids a second wave of renames across every DTO/mapper/frontend model on top of the identity-source change itself. Task 25 documents this explicitly in Postman docs and flags it to the frontend plan (this file's sibling in the frontend repo) as a breaking contract change requiring frontend updates even though field names are unchanged.

**Additional Global Constraint for this phase:** never introduce a second identity system alongside this one — every handler this phase touches must end up comparing `Guid employeeId` to `Guid employeeId`, never mixing a resolved `employeeId` against a raw `objective.OwnerId` that might still be UserId-valued mid-refactor. Complete Task 13 (the data backfill) before merging any handler change from Tasks 15–24, or reads/writes will silently disagree on what a stored Guid means.

---

## Task 13: Migration — backfill UserId-valued columns to EmployeeId, drop redundant UserId columns

**Files:**
- Create: new EF migration `ReplaceUserIdentityWithEmployeeIdentityInWorkManagement` (generated skeleton, hand-filled `Up`/`Down`)
- Modify: `src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs` (remove `UserId` property)
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberConfiguration.cs` (drop `UserId`-based indexes, add `EmployeeId`-based ones)
- Modify: `src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs` (remove `InvitedUserId` property)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederDapiGuardTests.cs` (add one assertion — see Step 5)

**Interfaces:**
- Consumes: nothing new — reads the existing `employees.user_id` mapping to compute the backfill.
- Produces: from this task onward, `objectives.owner_id`/`reporting_manager_id`, `objective_change_requests.requested_by_id`/`reporting_manager_id`/`decided_by_id`, and `projects.lead_id` all hold `employees.id` values, not `users.id`. `project_members`/`project_member_invitations` no longer have a `UserId`/`InvitedUserId` column at all — `EmployeeId`/`InvitedEmployeeId` is now the only identity column on those two tables. Every task from 14 onward assumes this.

- [ ] **Step 1: Remove the entity properties, update configurations**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

public static class ProjectMembershipSources
{
    public const string System = "system";
    public const string ObjectiveInvitation = "objective_invitation";
}

public class ProjectMember : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid EmployeeId { get; set; }
    public string MembershipSource { get; set; } = ProjectMembershipSources.System;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs — remove
// the `InvitedUserId` property only; everything else (InvitedEmployeeId, Status, InviteType from
// Task 1 above, etc.) is unchanged.
public class ProjectMemberInvitation : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedEmployeeId { get; set; }
    public string InviteType { get; set; } = ProjectInvitationTypes.Member;
    public string Status { get; set; } = ProjectInvitationStatuses.Pending;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MembershipSource).HasMaxLength(30).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_project_members_tenant_project_objective_employee");
        builder.HasIndex(m => new { m.TenantId, m.EmployeeId, m.IsActive, m.ProjectId })
            .HasDatabaseName("ix_project_members_tenant_employee_active_project");
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.IsActive })
            .HasDatabaseName("ix_project_members_tenant_project_objective_active");

        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(m => m.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Generate the migration skeleton**

Run: `dotnet ef migrations add ReplaceUserIdentityWithEmployeeIdentityInWorkManagement --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

This produces `Up`/`Down` with the `project_members`/`project_member_invitations` column drops already correct (from the entity/configuration changes in Step 1) and the new indexes. Delete anything it auto-generates for `Objective`/`Project`/`ObjectiveChangeRequest` (there shouldn't be any — those entities' C# types don't change, only stored values) and hand-write the backfill `Sql(...)` calls below into the same file, ordered **before** the `project_members`/`project_member_invitations` column drops (the backfill for those two tables reads their own `EmployeeId` column, which must still exist and be populated — it already is, from every place that constructs a `ProjectMember`/`ProjectMemberInvitation` today).

- [ ] **Step 3: Hand-write the `Up`/`Down` backfill**

```csharp
public partial class ReplaceUserIdentityWithEmployeeIdentityInWorkManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- Backfill: UserId-valued columns -> the matching Employee.Id, same tenant. ---
        // Rows whose stored UserId has no Employee in this tenant (e.g. a smoke-test user with no
        // Employee record) are left unchanged by design - Step 4 below finds and reports any such
        // rows so they can be fixed by hand before this migration is considered done.
        migrationBuilder.Sql(@"
            UPDATE projects p SET lead_id = e.id
            FROM employees e WHERE e.tenant_id = p.tenant_id AND e.user_id = p.lead_id;");

        migrationBuilder.Sql(@"
            UPDATE objectives o SET owner_id = e.id
            FROM employees e WHERE e.tenant_id = o.tenant_id AND e.user_id = o.owner_id;");
        migrationBuilder.Sql(@"
            UPDATE objectives o SET reporting_manager_id = e.id
            FROM employees e
            WHERE o.reporting_manager_id IS NOT NULL
              AND e.tenant_id = o.tenant_id AND e.user_id = o.reporting_manager_id;");

        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET requested_by_id = e.id
            FROM employees e WHERE e.tenant_id = r.tenant_id AND e.user_id = r.requested_by_id;");
        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET reporting_manager_id = e.id
            FROM employees e WHERE e.tenant_id = r.tenant_id AND e.user_id = r.reporting_manager_id;");
        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET decided_by_id = e.id
            FROM employees e
            WHERE r.decided_by_id IS NOT NULL
              AND e.tenant_id = r.tenant_id AND e.user_id = r.decided_by_id;");

        // --- project_members / project_member_invitations: drop the now-redundant UserId column. ---
        migrationBuilder.DropIndex(name: "ix_project_members_tenant_project_objective_user", table: "project_members");
        migrationBuilder.DropIndex(name: "ix_project_members_tenant_user_active_project", table: "project_members");
        migrationBuilder.DropColumn(name: "user_id", table: "project_members");
        migrationBuilder.CreateIndex(
            name: "ix_project_members_tenant_project_objective_employee", table: "project_members",
            columns: new[] { "tenant_id", "project_id", "objective_id", "employee_id" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_project_members_tenant_employee_active_project", table: "project_members",
            columns: new[] { "tenant_id", "employee_id", "is_active", "project_id" });

        migrationBuilder.DropColumn(name: "invited_user_id", table: "project_member_invitations");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "invited_user_id", table: "project_member_invitations",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.Sql(@"
            UPDATE project_member_invitations i SET invited_user_id = e.user_id
            FROM employees e WHERE e.id = i.invited_employee_id;");

        migrationBuilder.AddColumn<Guid>(name: "user_id", table: "project_members",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.Sql(@"
            UPDATE project_members m SET user_id = e.user_id
            FROM employees e WHERE e.id = m.employee_id;");
        migrationBuilder.DropIndex(name: "ix_project_members_tenant_project_objective_employee", table: "project_members");
        migrationBuilder.DropIndex(name: "ix_project_members_tenant_employee_active_project", table: "project_members");
        migrationBuilder.CreateIndex(
            name: "ix_project_members_tenant_project_objective_user", table: "project_members",
            columns: new[] { "tenant_id", "project_id", "objective_id", "user_id" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_project_members_tenant_user_active_project", table: "project_members",
            columns: new[] { "tenant_id", "user_id", "is_active", "project_id" });

        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET decided_by_id = e.user_id
            FROM employees e WHERE r.decided_by_id IS NOT NULL AND e.id = r.decided_by_id;");
        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET reporting_manager_id = e.user_id
            FROM employees e WHERE e.id = r.reporting_manager_id;");
        migrationBuilder.Sql(@"
            UPDATE objective_change_requests r SET requested_by_id = e.user_id
            FROM employees e WHERE e.id = r.requested_by_id;");

        migrationBuilder.Sql(@"
            UPDATE objectives o SET reporting_manager_id = e.user_id
            FROM employees e WHERE o.reporting_manager_id IS NOT NULL AND e.id = o.reporting_manager_id;");
        migrationBuilder.Sql(@"
            UPDATE objectives o SET owner_id = e.user_id
            FROM employees e WHERE e.id = o.owner_id;");

        migrationBuilder.Sql(@"
            UPDATE projects p SET lead_id = e.user_id
            FROM employees e WHERE e.id = p.lead_id;");
    }
}
```

- [ ] **Step 4: Apply the migration and verify against a real Postgres instance**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Then run this verification query directly against the dev database (matches this repo's established `pg_indexes`-verification precedent — never trust a migration from a passing build alone):

```sql
-- Any row returned here has a stale UserId-valued column that the backfill couldn't match to an
-- Employee (no Employee row for that UserId in that tenant) - fix these by hand before proceeding,
-- they will silently break ownership checks otherwise.
SELECT 'projects.lead_id' AS column_name, p.id, p.tenant_id, p.lead_id
FROM projects p WHERE NOT EXISTS (SELECT 1 FROM employees e WHERE e.id = p.lead_id)
UNION ALL
SELECT 'objectives.owner_id', o.id, o.tenant_id, o.owner_id
FROM objectives o WHERE NOT EXISTS (SELECT 1 FROM employees e WHERE e.id = o.owner_id)
UNION ALL
SELECT 'objectives.reporting_manager_id', o.id, o.tenant_id, o.reporting_manager_id
FROM objectives o WHERE o.reporting_manager_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM employees e WHERE e.id = o.reporting_manager_id);
```

Expected: 0 rows (this repo's dev/demo seed data always creates an Employee for every User it creates — see Task 24). If any row comes back, the affected Project/Objective was created for a User with no Employee record; fix the source data or exclude it before continuing to Task 15.

Also run: `SELECT indexname FROM pg_indexes WHERE tablename = 'project_members';` — expect `ix_project_members_tenant_project_objective_employee` and `ix_project_members_tenant_employee_active_project` present, `ix_project_members_tenant_project_objective_user` and `ix_project_members_tenant_user_active_project` gone.

- [ ] **Step 5: Add a regression assertion to the existing seeder guard test**

The existing `WorkManagementSampleDataSeederDapiGuardTests.cs` already asserts the dapi tenant is skipped by the generic seeder. Add one assertion confirming the invariant this migration depends on for every seeded row going forward:

```csharp
[Fact]
public async Task SeedAsync_EveryObjectiveOwnerId_HasMatchingEmployeeRecord()
{
    // Arrange - seed via the real seeder, exactly as production startup does.
    await using var db = CreateInMemoryOrTestDbContext(); // matches this test file's existing setup helper
    var tenantContext = CreateWritableTenantContextStub();
    await WorkManagementSampleDataSeeder.SeedAsync(db, tenantContext, CancellationToken.None);

    // Act
    var objectives = await db.Objectives.AsNoTracking().ToListAsync();
    var employeeIds = await db.Employees.AsNoTracking().Select(e => e.Id).ToListAsync();

    // Assert - every seeded OwnerId must be a real Employee.Id, never a bare UserId.
    Assert.All(objectives, o => Assert.Contains(o.OwnerId, employeeIds));
}
```

(Follow this test file's existing helper method names for constructing the test `DbContext`/`IWritableTenantContext` — do not invent new ones; match whatever `SeedAsync_SkipsDapiTenant`-style tests already in this file use.)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberConfiguration.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederDapiGuardTests.cs
git commit -m "feat(work): migrate Work Management ownership columns from UserId to EmployeeId identity"
```

---

## Task 14: `ICallerIdentityResolver` — resolve the session's UserId to their EmployeeId

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Common/Services/ICallerIdentityResolver.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Common/Services/CallerIdentityResolver.cs`
- Modify: `src/ONEVO.Api/DependencyInjection.cs` (or wherever this repo registers `IMilestoneMembershipCoordinator` — register alongside it)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Common/CallerIdentityResolverTests.cs`

**Interfaces:**
- Consumes: the existing `IEmployeeRepository.GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken)` (`ONEVO.Application.Common.RepositoryInterfaces`, already used by `MilestoneMembershipCoordinator` and `CreateProjectCommandHandler` today) — read-only, no Core HR file touched.
- Produces: `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken)` → `Guid?`. Every handler rewritten in Tasks 18–23 calls this once, immediately after the existing `tenantId`/`userId` guard, and treats `null` as `Result.Forbidden("No employee record for the current user.")` — the same message `CreateProjectCommandHandler` already uses today for the equivalent case.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Common/CallerIdentityResolverTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Common;

public class CallerIdentityResolverTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly CallerIdentityResolver _sut;

    public CallerIdentityResolverTests()
    {
        _sut = new CallerIdentityResolver(_employees.Object);
    }

    [Fact]
    public async Task ResolveCallerEmployeeIdAsync_EmployeeExists_ReturnsEmployeeId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId };
        _employees.Setup(e => e.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var result = await _sut.ResolveCallerEmployeeIdAsync(tenantId, userId, CancellationToken.None);

        Assert.Equal(employee.Id, result);
    }

    [Fact]
    public async Task ResolveCallerEmployeeIdAsync_NoEmployeeRecord_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _employees.Setup(e => e.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await _sut.ResolveCallerEmployeeIdAsync(tenantId, userId, CancellationToken.None);

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CallerIdentityResolverTests"`
Expected: FAIL — `CallerIdentityResolver`/`ICallerIdentityResolver` do not exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Common/Services/ICallerIdentityResolver.cs
namespace ONEVO.Application.Features.WorkManagement.Common.Services;

/// <summary>
/// Resolves the current session's UserId to the caller's Employee.Id within this tenant - the
/// single seam every Work Management handler goes through instead of comparing UserId directly
/// (see Phase 2 preamble, docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md).
/// </summary>
public interface ICallerIdentityResolver
{
    /// <summary>Null if the caller has no active Employee record in this tenant.</summary>
    Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Common/Services/CallerIdentityResolver.cs
using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Common.Services;

public class CallerIdentityResolver : ICallerIdentityResolver
{
    private readonly IEmployeeRepository _employees;

    public CallerIdentityResolver(IEmployeeRepository employees) => _employees = employees;

    public async Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        return employee?.Id;
    }
}
```

Register it wherever `IMilestoneMembershipCoordinator` is registered today (find via `grep -rn "IMilestoneMembershipCoordinator," src/ONEVO.Api/DependencyInjection.cs` or the DI module it actually lives in):

```csharp
services.AddScoped<ICallerIdentityResolver, CallerIdentityResolver>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CallerIdentityResolverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Common/ src/ONEVO.Api/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Common/
git commit -m "feat(work): add ICallerIdentityResolver - UserId to EmployeeId resolution seam"
```

---

## Task 15: `IMilestoneMembershipCoordinator` — drop the `userId` parameter, `EmployeeId`-only

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs` (update every existing test's call sites — same assertions, fewer parameters)

**Interfaces:**
- Consumes: `IProjectMemberRepository` (Task 16 below — its methods also drop `userId` in favor of `employeeId`).
- Produces: `GetActiveAssigneeAsync(tenantId, employeeId)` (was `userId`-keyed, now looks the Employee up **by Id** instead of by UserId — a plain `IEmployeeRepository.GetByIdAsync`, not `GetByUserIdAsync`), `UpsertMembershipAsync(tenantId, projectId, objectiveId, employeeId)` (dropped the redundant second `Guid userId` param — `ProjectMember` no longer has a `UserId` column per Task 13), `DeactivateMembershipAsync(tenantId, projectId, objectiveId, employeeId)`, `HasOtherActiveAccessAsync(tenantId, projectId, employeeId, excludingObjectiveId)`. Every caller in Tasks 18–23 uses these new signatures.

- [ ] **Step 1: Update the interface**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Encapsulates the membership-lifecycle rules from
/// docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md
/// §3, shared across Create/Transfer/Achieve/member-management. Never calls SaveChangesAsync -
/// callers wrap the whole operation in IUnitOfWork.ExecuteInTransactionAsync. EmployeeId-keyed
/// throughout (Phase 2, 2026-08-14) - callers resolve the caller's own EmployeeId via
/// ICallerIdentityResolver before calling in here; a target person's EmployeeId (e.g. the invitee
/// being added) already flows in from the wire as EmployeeId directly.
/// </summary>
public interface IMilestoneMembershipCoordinator
{
    /// <summary>Null if no active Employee exists with this Id in this tenant, or their EmploymentStatusId isn't Active.</summary>
    Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Creates a new milestone-scoped membership, or reactivates an existing inactive one. No-op if already active.</summary>
    Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deactivates the membership for this exact (project, objective, employee) triple. No-op if no row exists.</summary>
    Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    /// <summary>True if the employee has any other active membership in this project (direct or a different milestone).</summary>
    Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update the implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;

    public MilestoneMembershipCoordinator(IEmployeeRepository employees, IProjectMemberRepository members)
    {
        _employees = employees;
        _members = members;
    }

    public async Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        return employee is not null && employee.EmploymentStatusId == EmploymentStatusIds.Active ? employee : null;
    }

    public async Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, employeeId, ct);

        if (existing is null)
        {
            await _members.AddAsync(new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = projectId,
                ObjectiveId = objectiveId,
                EmployeeId = employeeId,
                MembershipSource = ProjectMembershipSources.ObjectiveInvitation,
                IsActive = true,
                JoinedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        if (existing.IsActive)
            return;

        existing.IsActive = true;
        existing.RemovedAt = null;
        existing.JoinedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public async Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, employeeId, ct);
        if (existing is null || !existing.IsActive)
            return;

        existing.IsActive = false;
        existing.RemovedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default)
        => _members.HasActiveMembershipExcludingObjectiveAsync(tenantId, projectId, employeeId, excludingObjectiveId, ct);
}
```

Note: `CreatedById` is dropped from the inline `ProjectMember` construction above — it's a `BaseEntity` audit field (out of this phase's scope per the preamble) and the coordinator never had reliable access to "who is performing this action" vs. "who the membership is for" as two separate values once `userId` is gone from its signature; callers that need `CreatedById` set to the *acting* caller's UserId (an audit concern, not a business one) should set it themselves after `UpsertMembershipAsync` returns, the same way `IUnitOfWork.SaveChangesAsync` already stamps audit columns elsewhere in this codebase — check `IUnitOfWork`'s `SaveChangesAsync` implementation for whether `CreatedById` is auto-stamped from `ICurrentUser` already (grep `CreatedById` in `EfUnitOfWork.cs` or equivalent) before assuming this coordinator must set it explicitly.

- [ ] **Step 3: Update every existing test call site**

Open `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs`, find every call to `GetActiveAssigneeAsync`, `UpsertMembershipAsync`, `DeactivateMembershipAsync`, `HasOtherActiveAccessAsync` and drop the redundant `userId` argument (keep whichever single Guid argument the test was using as the employee identity — rename the local variable from `userId`/`_testUserId` to `employeeId`/`_testEmployeeId` for clarity, and update the `Mock<IEmployeeRepository>` setups from `GetByUserIdAsync` to `GetByIdAsync` in `GetActiveAssigneeAsync`'s tests specifically).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MilestoneMembershipCoordinator"`
Expected: PASS (will not compile until Task 16 also lands, since `IProjectMemberRepository`'s signatures change together — run Tasks 15 and 16 as one combined build/test cycle, commit separately).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs
git commit -m "feat(work): IMilestoneMembershipCoordinator - EmployeeId-only, drop UserId parameter"
```

---

## Task 16: `IProjectMemberRepository` + `EfProjectMemberRepository` — `EmployeeId`-only

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfProjectMemberRepositoryTests.cs` (if it exists — update call sites; if this repository has no dedicated test file today, skip this file and rely on the handler-level tests in Tasks 18–23 to exercise it)

**Interfaces:**
- Produces: every method below, renamed and re-typed from `userId` to `employeeId` — Task 15's coordinator and every handler in Tasks 18–23 depend on these exact names.

- [ ] **Step 1: Update the interface**

```csharp
// src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);

    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Tracked - see original doc comment on the equivalent UserId-keyed method this replaces (design intent unchanged, only the identity column changed).</summary>
    Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default);

    Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMember>> ListForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    void Update(ProjectMember member);

    /// <summary>Batched, per-project, deduplicated-by-employee list of active member employee ids, capped at takePerProject, earliest joiners first.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListDistinctActiveMemberEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default);

    /// <summary>Batched, per-project count of distinct active member employees.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountDistinctActiveMembersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update the EF implementation**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId && m.IsActive, ct);
    }

    public async Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.ObjectiveId == objectiveId && m.EmployeeId == employeeId, ct);
    }

    public async Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, Guid excludingObjectiveId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId
                        && m.ObjectiveId != excludingObjectiveId && m.IsActive, ct);
    }

    public async Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid employeeId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId
                        && m.IsActive && objectiveIds.Contains(m.ObjectiveId), ct);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId && m.IsActive)
            .Select(m => m.ObjectiveId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId && !m.IsActive && m.RemovedAt != null)
            .OrderByDescending(m => m.RemovedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId)
            .ToListAsync(ct);
    }

    public void Update(ProjectMember member)
    {
        _db.ProjectMembers.Update(member);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListDistinctActiveMemberEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();

        var rows = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && projectIds.Contains(m.ProjectId) && m.IsActive)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new { m.ProjectId, m.EmployeeId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(r => r.EmployeeId).Distinct().Take(takePerProject).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountDistinctActiveMembersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, int>();

        var rows = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && projectIds.Contains(m.ProjectId) && m.IsActive)
            .Select(m => new { m.ProjectId, m.EmployeeId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
    }
}
```

- [ ] **Step 3: Find and update every caller of the renamed methods**

Run: `grep -rln "GetActiveObjectiveIdsForUserInProjectAsync\|ListInactiveMembershipsForUserAsync\|ListForUserInProjectAsync\|ListDistinctActiveMemberUserIdsAsync" src/ONEVO.Application/Features/WorkManagement` — update every call site found to the renamed method (same arguments, `userId` variable renamed to `employeeId` where it's now sourced from `ICallerIdentityResolver` or an already-Employee-typed value instead of `_currentUser.UserId`). These call sites are covered individually in Task 23's mechanical sweep table if not already rewritten in Tasks 18–22.

- [ ] **Step 4: Build and run the full Work Management + Common test slice**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: will not fully pass until Tasks 17–23 also update their call sites — this is expected at this point in the phase; re-run this same command again after Task 23 and expect 0 failures then.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs
git commit -m "feat(work): IProjectMemberRepository - EmployeeId-only, drop UserId parameter"
```

---

## Task 17: `ObjectiveMapper` / `ProjectMapper` — name-lookup dictionaries become `Guid employeeId`-keyed

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveMapperTests.cs` (if it exists — update call sites; the mapper's output shape is unchanged, only the lookup dictionary's key type)

**Interfaces:**
- Consumes: nothing new.
- Produces: `ObjectiveMapper.ToDetail(objective, namesByEmployeeId, callerEmployeeId)` (renamed from `namesByUserId`/`currentUserId`), `ObjectiveMapper.ToSubtreeNode(..., namesByEmployeeId, callerEmployeeId)` — every query handler in Tasks 18–23 that builds a name-lookup dictionary now keys it by `Employee.Id`, not `Employee.UserId`.

- [ ] **Step 1: Rewrite the mapper**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

public static class ObjectiveMapper
{
    public static ObjectiveDetailResponse ToDetail(
        Objective objective, IReadOnlyDictionary<Guid, string>? namesByEmployeeId = null, Guid? callerEmployeeId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByEmployeeId), ResolveName(objective.ReportingManagerId, namesByEmployeeId),
        callerEmployeeId.HasValue && objective.OwnerId == callerEmployeeId.Value);

    private static string? ResolveName(Guid? employeeId, IReadOnlyDictionary<Guid, string>? namesByEmployeeId)
        => employeeId.HasValue && namesByEmployeeId is not null && namesByEmployeeId.TryGetValue(employeeId.Value, out var name) ? name : null;

    public static ObjectiveTreeItemResponse ToTreeItem(Objective objective) => new(
        objective.Id, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours, objective.IsActive, objective.IsAchieved);

    public static ObjectiveSubtreeNodeResponse ToSubtreeNode(
        Objective objective, ILookup<Guid, Objective> childrenByParent,
        IReadOnlyDictionary<Guid, string>? namesByEmployeeId = null, Guid? callerEmployeeId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByEmployeeId), ResolveName(objective.ReportingManagerId, namesByEmployeeId),
        callerEmployeeId.HasValue && objective.OwnerId == callerEmployeeId.Value,
        objective.IsAchieved, objective.AchievedAt,
        childrenByParent[objective.Id].Select(c => ToSubtreeNode(c, childrenByParent, namesByEmployeeId, callerEmployeeId)).ToList());

    public static ObjectiveChangeRequestResponse ToResponse(ObjectiveChangeRequest request) => new(
        request.Id, request.ObjectiveId, request.RequestType, request.RequestedById, request.ReportingManagerId,
        request.Status, request.PayloadJson, request.DecidedAt, request.DecidedById, request.CreatedAt);
}
```

`ProjectMapper.cs`: no signature change needed — it only ever passes `project.LeadId`/`objective.OwnerId` straight through as opaque Guids into DTOs (`ToSummary`, `ToDetail`, `ToListItem`), never resolves a name itself. Confirm this by re-reading the file; if any method there also takes a `namesByUserId`-style dictionary parameter, rename it to `namesByEmployeeId` the same way as above.

- [ ] **Step 2: Update test call sites, build, run tests**

Run: `grep -rln "namesByUserId\|currentUserId" tests/ONEVO.Tests.Unit/Features/WorkManagement` and rename each to `namesByEmployeeId`/`callerEmployeeId` to match. Then:
`dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/
git commit -m "refactor(work): ObjectiveMapper name-lookup dictionaries are EmployeeId-keyed"
```

---

## Task 18: `CreateProjectCommandHandler` + `CreateObjectiveCommandHandler` — `LeadId`/`OwnerId`/`ReportingManagerId` set from the resolved caller EmployeeId

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs` (also supersedes this file's own Task 11 edits above — `HeadEmployeeId` from Task 11 is now the *only* form this ever took; no further change needed there beyond what's shown here)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs`, `CreateObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` (Task 14), `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync(tenantId, employeeId)` (Task 15).
- Produces: `Project.LeadId`, `Objective.OwnerId`, `Objective.ReportingManagerId` now hold the caller's resolved `Employee.Id`.

- [ ] **Step 1: Rewrite `CreateProjectCommandHandler`**

Only the `Handle` method body changes (constructor/fields unchanged — `_employees` is already injected):

```csharp
    public async Task<Result<ProjectCreationResponse>> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectCreationResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectCreationResponse>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        if (employee is null || employee.EmploymentStatusId != EmploymentStatusIds.Active)
            return Result<ProjectCreationResponse>.Forbidden("No employee record for the current user.");

        var employeeId = employee.Id;

        var legalEntity = await _legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
        if (legalEntity is null)
            return Result<ProjectCreationResponse>.Forbidden("Tenant has no primary company configured.");

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null || !category.IsActive)
            return Result<ProjectCreationResponse>.NotFound("Project category not found.");

        var identifier = request.Identifier.Trim().ToUpperInvariant();
        if (await _projects.IdentifierExistsForTenantAsync(tenantId, identifier, ct))
            return Result<ProjectCreationResponse>.Conflict("A project with this identifier already exists.");

        var normalizedLabelNames = request.Labels
            .Select(l => l.Name.Trim().ToLowerInvariant())
            .ToList();
        if (normalizedLabelNames.Distinct().Count() != normalizedLabelNames.Count)
            return Result<ProjectCreationResponse>.Conflict("Duplicate label names are not allowed in the same request.");

        FileRecordDto? uploadedLogo = null;
        if (request.LogoContent is not null && request.LogoFileName is not null && request.LogoContentType is not null)
        {
            var uploadResult = await _fileStorage.UploadAsync(
                tenantId, userId, request.LogoFileName, request.LogoContentType,
                UploadPurposeCatalog.ProjectCover, request.LogoContent, ct);

            if (!uploadResult.IsSuccess)
                return Result<ProjectCreationResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

            uploadedLogo = uploadResult.Value;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwningLegalEntityId = legalEntity.Id,
                CategoryId = category.Id,
                Name = request.Name.Trim(),
                Identifier = identifier,
                Description = request.Description?.Trim(),
                LeadId = employeeId,
                StartDate = request.StartDate,
                TargetDate = request.TargetDate,
                Color = request.Color,
                ActualHours = request.ActualHours,
                AllocatedHours = 0m,
                CompletedHours = 0m,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultObjective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ParentObjectiveId = null,
                IsDefault = true,
                Title = project.Name,
                Description = project.Description,
                OwnerId = employeeId,
                IsActive = true,
                StartDate = project.StartDate,
                EndDate = project.TargetDate,
                Progress = 0m,
                ActualHours = project.ActualHours,
                AllocatedHours = request.DefaultObjectiveAllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            var creatorMembership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ObjectiveId = defaultObjective.Id,
                EmployeeId = employeeId,
                MembershipSource = ProjectMembershipSources.System,
                IsActive = true,
                JoinedAt = now,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultVersion = new ProjectVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = "Initial Release",
                StatusId = VersionStatusIds.Planned,
                CreatedById = userId,
                CreatedAt = now
            };

            var releaseReminder = new ReleaseCalendarEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                VersionId = defaultVersion.Id,
                RecipientUserId = userId,
                ScheduledDate = request.ReleaseDate,
                ReminderType = ReleaseReminderTypes.ProjectRelease,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var labels = request.Labels.Select(l => new Label
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = l.Name.Trim(),
                Color = l.Color,
                CreatedById = userId,
                CreatedAt = now
            }).ToList();

            EntityAsset? logoAsset = null;
            if (uploadedLogo is not null)
            {
                logoAsset = new EntityAsset
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OwnerType = EntityAssetOwnerTypes.Project,
                    OwnerId = project.Id,
                    AssetPurpose = UploadPurposeCatalog.ProjectCover,
                    FileRecordId = uploadedLogo.Id,
                    IsPrimary = true,
                    CreatedByType = "user",
                    CreatedById = userId,
                    CreatedAt = now
                };
            }

            await _projects.AddAsync(project, ct);
            await _objectives.AddAsync(defaultObjective, ct);
            await _members.AddAsync(creatorMembership, ct);
            await _versions.AddAsync(defaultVersion, ct);
            await _releaseCalendar.AddAsync(releaseReminder, ct);
            foreach (var label in labels)
                await _labels.AddAsync(label, ct);
            if (logoAsset is not null)
                await _entityAssets.AddAsync(logoAsset, ct);

            await _auditLogs.AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Action = "project.created",
                ResourceType = "Project",
                ResourceId = project.Id,
                NewValuesJson = $"{{\"name\":\"{project.Name}\",\"identifier\":\"{project.Identifier}\"}}",
                CreatedAt = now
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            var response = new ProjectCreationResponse(
                ProjectMapper.ToSummary(project),
                ProjectMapper.ToSummary(defaultObjective),
                ProjectMapper.ToSummary(defaultVersion, "planned"),
                ProjectMapper.ToSummary(releaseReminder),
                labels.Select(ProjectMapper.ToSummary).ToList(),
                ProjectMapper.ToSummary(creatorMembership),
                uploadedLogo is not null ? new ProjectLogoSummaryDto(uploadedLogo.Id, uploadedLogo.OriginalFileName) : null);

            return Result<ProjectCreationResponse>.Success(response);
        }
        catch
        {
            if (uploadedLogo is not null)
            {
                _logger?.LogError(
                    "Project creation failed after logo upload completed. Orphaned file_record {FileRecordId} for tenant {TenantId} requires manual/future reconciliation.",
                    uploadedLogo.Id, tenantId);
            }
            throw;
        }
    }
```

(Only 3 lines actually changed from the current file: `LeadId = employeeId` (was `userId`), `OwnerId = employeeId` (was `userId`), `EmployeeId = employeeId` with the `UserId = userId,` line removed from `creatorMembership` — plus the new `employee`/`employeeId` resolution near the top, which this handler already had 90% of since it already looked up `_employees.GetByUserIdAsync` for the `EmploymentStatusIds.Active` check. Every other field — `CreatedById`, `AuditLog.UserId`, `ReleaseCalendarEntry.RecipientUserId` — deliberately keeps `userId`, per the Phase 2 scope boundary.)

- [ ] **Step 2: Rewrite `CreateObjectiveCommandHandler`**

```csharp
    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveDetailResponse>.Forbidden("No employee record for the current user.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        // Free-control rule (design §4): only the parent's current Head may create a child under it.
        if (parent.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        // Creator always starts as owner (design amendment, Task 11 above) - HeadEmployeeId from
        // the request, if given, is handled entirely by the member-invitations loop Task 11 added,
        // never by assigning ownership directly here.
        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, callerEmployeeId.Value, ct);
        if (assignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The assigned head must be an active employee in this tenant.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            var objective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = parent.ProjectId,
                ParentObjectiveId = parent.Id,
                IsDefault = false,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                OwnerId = callerEmployeeId.Value,
                // Always the creator's EmployeeId, later kept in sync with the PARENT's current
                // head by Transfer's cascade (design §4, Task 19 below), not by anything in this handler.
                ReportingManagerId = callerEmployeeId.Value,
                IsActive = true,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Progress = 0m,
                AllocatedHours = request.AllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            await _objectives.AddAsync(objective, innerCt);

            await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, callerEmployeeId.Value, innerCt);
            await _autoGrant.EnsureGrantedAsync(tenantId, userId, userId, "projects:access", innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
```

Add `private readonly ICallerIdentityResolver _identity;` to the constructor (same pattern as every other injected dependency in this file) and inject it via DI. Note `_autoGrant.EnsureGrantedAsync` still takes `(tenantId, userId, userId, ...)` — auto-grant is a permission-system concept keyed on `UserId` (it grants a *login session* a permission, not an Employee record) and is explicitly out of this phase's scope; do not change its signature or call shape.

- [ ] **Step 3: Update both handlers' existing tests**

For each test file, update every `Mock<IEmployeeRepository>`/assignee setup that previously keyed off a raw `userId` to instead set up `Mock<ICallerIdentityResolver>.Setup(i => i.ResolveCallerEmployeeIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId)`, and change every assertion that checked `objective.OwnerId == userId` / `project.LeadId == userId` to check `== employeeId` instead. Add the two constructor-injected mocks (`Mock<ICallerIdentityResolver>` for `CreateObjectiveCommandHandlerTests`; `CreateProjectCommandHandlerTests` already mocks `IEmployeeRepository`, no new mock needed there).

- [ ] **Step 4: Build and test**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateProjectCommandHandlerTests|FullyQualifiedName~CreateObjectiveCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/ src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/ tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs
git commit -m "feat(work): CreateProject/CreateObjective - LeadId/OwnerId/ReportingManagerId are EmployeeId"
```

---

## Task 19: `TransferObjectiveHeadCommandHandler` — EmployeeId end-to-end (supersedes this file's Task 10 amendment)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs` (field is `NewHeadEmployeeId` — if Task 10 above already renamed it, no further change here; if Task 10 was executed before this phase existed and still says `NewHeadUserId`, rename it now)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICallerIdentityResolver` (Task 14), `IMilestoneMembershipCoordinator` with the Task 15 signatures.
- Supersedes: Task 10's `GetActiveByEmployeeIdAsync` boundary-hop pattern — that method is no longer needed anywhere; `request.NewHeadEmployeeId` now flows straight into `_membership.GetActiveAssigneeAsync(tenantId, employeeId)` with no intermediate translation.

- [ ] **Step 1: Rewrite the handler**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        if (objective.CreatedById == userId)
        {
            var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadEmployeeId, ct);
            if (newHeadAssignee is null)
                return Result<ObjectiveChangeOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadEmployeeId = objective.OwnerId;

                objective.OwnerId = request.NewHeadEmployeeId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                // Reporting Manager cascade (design §4): direct children only, one level.
                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = request.NewHeadEmployeeId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.NewHeadEmployeeId, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, newHeadAssignee.UserId, userId, "projects:access", innerCt);

                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new TransferObjectiveRequestPayload(request.NewHeadEmployeeId);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Transfer,
            RequestedById = callerEmployeeId.Value,
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveChangeOutcomeResponse>.Success(
            new ObjectiveChangeOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
```

Also update `TransferObjectiveRequestPayload` (wherever it's declared — check `ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs`) if its constructor parameter is still named `NewHeadUserId`; rename to `NewHeadEmployeeId` for consistency, and update `ApproveObjectiveChangeRequestCommandHandler` (Task 20 below) accordingly — the two must agree on the field name since one serializes it and the other deserializes it.

Note the one behavioral-looking but actually-neutral change: `_autoGrant.EnsureGrantedAsync(tenantId, newHeadAssignee.UserId, userId, ...)` — auto-grant stays `UserId`-keyed (it's a login-session permission grant, out of scope per the preamble), so this line resolves `newHeadAssignee.UserId` (the `Employee` entity already carries this) rather than passing `request.NewHeadEmployeeId` directly, which would now be the wrong ID type for that call.

- [ ] **Step 2: Update the test file**

Rewrite every test's arrangement to mock `ICallerIdentityResolver` instead of relying on raw `userId`, change every `objective.OwnerId`/`ReportingManagerId` fixture value and assertion from a UserId-flavored Guid to an EmployeeId-flavored one, and change `request.NewHeadUserId` fixtures to `request.NewHeadEmployeeId`. Keep every existing test *scenario* (self-transfer with no RM → immediate; with RM and caller is creator → immediate; with RM and caller is not creator → creates change request; achieved/Default Objective rejections) — only the identity plumbing changes, not the behavior being tested.

- [ ] **Step 3: Build and test**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TransferObjectiveHeadCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/ src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs
git commit -m "feat(work): TransferObjectiveHead - EmployeeId end-to-end, no boundary resolution hop"
```

---

## Task 20: `ApproveObjectiveChangeRequestCommandHandler` + `RejectObjectiveChangeRequestCommandHandler` — ID types only, approval logic untouched

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RejectObjectiveChangeRequest/RejectObjectiveChangeRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs`, `RejectObjectiveChangeRequestCommandHandlerTests.cs`

**Why this file is touched despite the original design spec calling the Reporting-Manager approval flow "out of scope":** that constraint (§10 of the design spec, repeated in this file's own header comment) was about not changing *approval routing behavior* — who gets to approve what, and when. This task changes **nothing about that logic**. It only updates which identity space the `Guid`s being compared belong to, because `objective.ReportingManagerId`/`ObjectiveChangeRequest.ReportingManagerId` moved from UserId to EmployeeId in Task 13 — if this handler weren't updated, it would compare an EmployeeId-valued `changeRequest.ReportingManagerId` against a UserId-valued `userId`, and approval would silently stop working for every request. This is the one file in the whole phase where "don't touch this handler" (the earlier constraint) and "you must touch this handler" (this phase's requirement) coexist — call this out explicitly when reporting this task's completion, the same way Task 11's `CreateObjectiveCommandHandler`/`OwnerId` behavior change was flagged.

- [ ] **Step 1: Rewrite `ApproveObjectiveChangeRequestCommandHandler`**

Constructor gains `ICallerIdentityResolver _identity` (same pattern as Task 18/19). In `Handle`:

```csharp
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        // ... existing lookup of changeRequest/objective is unchanged ...

        if (changeRequest.ReportingManagerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this request's reporting manager can approve it.");
```

Further down, in the `Transfer` case of the switch (the only case that reads/writes `OwnerId`/`ReportingManagerId`/a name-lookup dictionary):

```csharp
                case ObjectiveChangeRequestTypes.Transfer:
                    var transferPayload = JsonSerializer.Deserialize<TransferObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                    var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, transferPayload.NewHeadEmployeeId, innerCt);
                    if (newHeadAssignee is null)
                        return Result.Failure("The new head must be an active employee in this tenant.");

                    var oldHeadEmployeeId = objective.OwnerId;
                    objective.OwnerId = transferPayload.NewHeadEmployeeId;
                    objective.UpdatedAt = now;

                    var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                    foreach (var child in directChildren)
                    {
                        child.ReportingManagerId = transferPayload.NewHeadEmployeeId;
                        child.UpdatedAt = now;
                    }

                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, transferPayload.NewHeadEmployeeId, innerCt);
                    await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                    await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);
                    break;
```

And in the `Unachieve` case:

```csharp
                case ObjectiveChangeRequestTypes.Unachieve:
                    var headAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, innerCt);
                    if (headAssignee is null)
                        // ... existing failure branch unchanged ...

                    objective.UpdatedAt = now;
                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                    break;
```

(`UpsertMembershipAsync`/`DeactivateMembershipAsync`/`HasOtherActiveAccessAsync`/`GetActiveAssigneeAsync` calls above drop their old trailing `Guid userId`/`Guid employeeId` two-argument shape from before Task 15 down to the single `employeeId` argument Task 15's new interface takes — remove any now-extra argument left over from the pre-Phase-2 call shape.)

- [ ] **Step 2: Rewrite `RejectObjectiveChangeRequestCommandHandler`**

Same pattern — add `ICallerIdentityResolver`, resolve `callerEmployeeId`, change:

```csharp
        if (changeRequest.ReportingManagerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this request's reporting manager can reject it.");
```

Reject has no `OwnerId`/`ReportingManagerId` mutation (per the design spec's §4.5 — "No side effects"), so no further change is needed beyond the identity resolution and comparison above.

- [ ] **Step 3: Update `TransferObjectiveRequestPayload`'s serialized field name**

If Task 19 renamed the payload record's property to `NewHeadEmployeeId`, this handler's `JsonSerializer.Deserialize<TransferObjectiveRequestPayload>` call picks it up automatically (same record type, no separate change needed here) — just confirm the property name used in `transferPayload.NewHeadEmployeeId` above matches exactly what Task 19 produced.

- [ ] **Step 4: Update both test files**

Same treatment as Task 19 Step 2 — mock `ICallerIdentityResolver`, change fixture Guids from UserId-flavored to EmployeeId-flavored for `ReportingManagerId`/`OwnerId`/`RequestedById` comparisons. Keep every existing scenario (approve success, approve wrong-RM Forbidden, approve already-decided Conflict, reject success, reject wrong-RM Forbidden) — only the identity plumbing changes.

- [ ] **Step 5: Build and test**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApproveObjectiveChangeRequestCommandHandlerTests|FullyQualifiedName~RejectObjectiveChangeRequestCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveChangeRequestCommandHandlerTests.cs
git commit -m "fix(work): Approve/Reject ObjectiveChangeRequest compare EmployeeId, not UserId - approval routing logic unchanged"
```

---

## Task 21: `GetObjectiveByIdQueryHandler` + `GetProjectByIdQueryHandler` — read-side, name-lookup + `isLead`/`isMyHead` comparisons

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/GetProjectByIdQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs`, `GetProjectByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICallerIdentityResolver`, `IEmployeeRepository.GetByUserIdsAsync` — reused as-is for the batch name lookup (it already returns `Employee` rows; the only change is which field of the result becomes the dictionary key: `e.Id` instead of `e.UserId`) — see Step 1.

- [ ] **Step 1: Rewrite `GetObjectiveByIdQueryHandler`**

```csharp
    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveDetailResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, callerEmployeeId.Value, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        var nameLookupIds = new List<Guid> { objective.OwnerId };
        if (objective.ReportingManagerId.HasValue)
            nameLookupIds.Add(objective.ReportingManagerId.Value);

        var employees = await _employees.GetByIdsAsync(tenantId, nameLookupIds, ct);
        var namesByEmployeeId = employees.ToDictionary(e => e.Id, e => $"{e.FirstName} {e.LastName}");

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective, namesByEmployeeId, callerEmployeeId.Value));
    }
```

Add `private readonly ICallerIdentityResolver _identity;` to the constructor. Note the batch lookup changes from `_employees.GetByUserIdsAsync(tenantId, nameLookupIds, ct)` to `_employees.GetByIdsAsync(tenantId, nameLookupIds, ct)` — `nameLookupIds` now holds `Employee.Id` values (from `objective.OwnerId`/`ReportingManagerId`), not `User.Id` values, so the batch lookup must fetch by Employee primary key, not by `UserId`. **`IEmployeeRepository.GetByIdsAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken)` does not exist yet on the Common `IEmployeeRepository` interface** (only the single-item `GetByIdAsync` and the `UserId`-keyed `GetByUserIdsAsync` exist today, per `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`). This is Core HR's interface (out of this phase's direct-edit scope per the Global Constraints in both this file and the original design spec) — **do not add the method there yourself.** Instead:

- [ ] **Step 1a: Add the batch-by-id lookup as a Work-Management-local helper, not a Core HR interface change**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Common/Services/ICallerIdentityResolver.cs
// (same file as Task 14 - add this second method to the same interface, since both are
// "resolve identity for Work Management" concerns and share the one IEmployeeRepository dependency)
public interface ICallerIdentityResolver
{
    Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Batch name lookup by Employee.Id (not UserId) - for resolving OwnerId/ReportingManagerId
    /// display names without a second round trip per id. Employees are looked up individually via the
    /// existing single-item IEmployeeRepository.GetByIdAsync in a loop rather than a new batch method on
    /// Core HR's interface, per this phase's scope guardrail (no Core HR file changes).</summary>
    Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default);
}
```

```csharp
// CallerIdentityResolver.cs - add the implementation
public async Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesByEmployeeIdAsync(
    Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
{
    var result = new Dictionary<Guid, string>();
    foreach (var employeeId in employeeIds.Distinct())
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is not null)
            result[employeeId] = $"{employee.FirstName} {employee.LastName}";
    }
    return result;
}
```

Then in `GetObjectiveByIdQueryHandler`, replace the two lines above with:

```csharp
        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds, ct);
```

(remove the now-unused `_employees` field/constructor parameter from this handler if nothing else in the file uses it — check before deleting.) **Apply this same `GetByIdsAsync`-doesn't-exist finding to every other handler in this phase that builds a name-lookup dictionary** (`GetMyProjectMilestonesQueryHandler`, `GetObjectiveSubtreeQueryHandler` — both covered in Task 23's sweep table below) — all of them use `ResolveDisplayNamesByEmployeeIdAsync` instead of a direct repository call.

- [ ] **Step 2: Rewrite `GetProjectByIdQueryHandler`**

Add `ICallerIdentityResolver`, resolve `callerEmployeeId`, then:

```csharp
        var isLead = project.LeadId == callerEmployeeId.Value;
```

(was `project.LeadId == userId`). If this handler also builds a members name-lookup dictionary further down (re-read the file's full body — only lines 84-86 were captured during scoping; confirm before assuming this is the only change needed), apply the same `ResolveDisplayNamesByEmployeeIdAsync` treatment.

- [ ] **Step 3: Update test files, build, run**

Same pattern as prior tasks — mock `ICallerIdentityResolver`, update fixture Guids. Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveByIdQueryHandlerTests|FullyQualifiedName~GetProjectByIdQueryHandlerTests"`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/ src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/ src/ONEVO.Application/Features/WorkManagement/Common/Services/ tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetProjectByIdQueryHandlerTests.cs
git commit -m "feat(work): GetObjectiveById/GetProjectById - EmployeeId-based access checks and name lookups"
```

---

## Task 22: `AddObjectiveMemberCommandHandler` + `RemoveObjectiveMemberCommandHandler` — drop the Task 4/10 boundary hop, EmployeeId flows straight through

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs`, `RemoveObjectiveMemberCommandHandlerTests.cs`

**This task supersedes Task 4's amendment above** — that task resolved the incoming `employeeId` to a `userId` at the handler boundary (`GetActiveByEmployeeIdAsync`) purely because `ProjectMember.UserId` was still the storage column at the time. Task 13 dropped that column; `EmployeeId` is now the only identity `ProjectMember` stores, so the boundary hop is dead code — delete it.

- [ ] **Step 1: Rewrite `AddObjectiveMemberCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot add members to an achieved milestone.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's head can add members.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result.Failure("The member must be an active employee in this tenant.");

        await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.EmployeeId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

(`AddObjectiveMemberCommand.EmployeeId` — confirm this is already the property name from Task 4's amendment; if Task 4 was executed before this phase and still calls it `request.UserId`, rename the command's property to `EmployeeId` here.)

- [ ] **Step 2: Rewrite `RemoveObjectiveMemberCommandHandler`**

```csharp
    public async Task<Result> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot remove members from an achieved milestone.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's head can remove members.");

        if (request.EmployeeId == objective.OwnerId)
            return Result.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.EmployeeId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
```

(Same constructor addition as Step 1 — `ICallerIdentityResolver`. `RemoveObjectiveMemberCommand.EmployeeId`/route parameter — this is Task 2's `DELETE /objectives/{id}/members/{employeeId}` route; if it was built against `{userId}` before this phase, rename the route parameter and controller action's binding too — check `ObjectivesController.cs`'s `RemoveMember` action.)

- [ ] **Step 3: Update both test files, build, run**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests|FullyQualifiedName~RemoveObjectiveMemberCommandHandlerTests"`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/ src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/ tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): AddObjectiveMember/RemoveObjectiveMember - EmployeeId flows straight through, no boundary hop"
```

---

## Task 23: Mechanical sweep — every remaining `_currentUser.UserId`/`.OwnerId`/`.ReportingManagerId`/`.LeadId` touch point

**Files and exact changes.** For every handler below: (a) add `ICallerIdentityResolver _identity` to the constructor the same way Tasks 18–22 did, (b) immediately after the existing `if (tenantId == Guid.Empty) return ...Forbidden(...)` guard, insert:

```csharp
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<...>.Forbidden("No employee record for the current user."); // match this handler's existing Result<T> vs Result shape
```

then (c) apply the specific line change listed for that file. Every file also needs its test file's fixtures/assertions updated the same way as Tasks 18–22 (UserId-flavored Guids for `Owner`/`Lead`/`ReportingManager` comparisons become EmployeeId-flavored, add an `ICallerIdentityResolver` mock) — not repeated per-file below to keep this table scannable, but required for every row.

| File | Line(s) (pre-Phase-2) | Change |
|---|---|---|
| `Projects/Commands/DeleteProject/DeleteProjectCommandHandler.cs` | `var userId = _currentUser.UserId;` (28), `if (project.LeadId != userId)` (~36) | Add resolver step; `if (project.LeadId != callerEmployeeId.Value)` |
| `Projects/Commands/UnachieveProject/UnachieveProjectCommandHandler.cs` | `var userId = _currentUser.UserId;` (28), `if (project.LeadId != userId)` (36) | Same pattern |
| `Projects/Commands/AchieveProject/AchieveProjectCommandHandler.cs` | `var userId = _currentUser.UserId;` (32), `if (project.LeadId != userId)` (40) | Same pattern |
| `Projects/Commands/EditProject/EditProjectCommandHandler.cs` | `var userId = _currentUser.UserId;` (40), `if (project.LeadId != userId)` (55) | Same pattern |
| `Projects/Queries/GetProjectLogo/GetProjectLogoQueryHandler.cs` | `var userId = _currentUser.UserId;` (47) | Re-read the file to find what `userId` is used for below line 47 (not captured during scoping — likely a `project.LeadId`/membership access check); apply the same resolver substitution to whatever comparison it feeds |
| `Projects/Queries/ListProjects/ListProjectsQueryHandler.cs` | `var targetUserId = request.TargetUserId ?? _currentUser.UserId;` (53), `p.LeadId == targetUserId` (72) | Rename `ListProjectsQuery.TargetUserId` → `TargetEmployeeId` (breaking API contract change — flag in Task 25); `var targetEmployeeId = request.TargetEmployeeId ?? callerEmployeeId.Value; ... p.LeadId == targetEmployeeId` |
| `Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs` | `var userId = _currentUser.UserId;` (35) | Re-read to find downstream usage (likely feeds a `HasActiveMembershipForAnyObjectiveAsync`-style access check per the `GetObjectiveById` pattern in Task 21) — apply `callerEmployeeId.Value` there |
| `Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs` | `var userId = _currentUser.UserId;` (39), `.SelectMany(o => new[] { (Guid?)o.OwnerId, o.ReportingManagerId })` (72, name-lookup id collection) | Add resolver; the `SelectMany` line itself needs no change (already collecting `Guid?` ids generically) but the dictionary those ids feed must be built via `_identity.ResolveDisplayNamesByEmployeeIdAsync` (Task 21's Step 1a helper), not `_employees.GetByUserIdsAsync` |
| `Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs` | `var userId = _currentUser.UserId;` (34), `nameLookupIds.Add(objective.OwnerId)` (51), `.ReportingManagerId.Value)` (53), `namesByUserId.TryGetValue(objective.OwnerId, ...)` (65), `.ReportingManagerId.Value, out reportingManagerName)` (68), `objective.OwnerId, ownerName, ...` (72), `objective.OwnerId == userId` (75) | Add resolver; rename `namesByUserId` local variable to `namesByEmployeeId`, build it via `ResolveDisplayNamesByEmployeeIdAsync`; final line becomes `objective.OwnerId == callerEmployeeId.Value` |
| `Objectives/Queries/GetMyObjectiveHistory/GetMyObjectiveHistoryQueryHandler.cs` | `var userId = _currentUser.UserId;` (29) | Re-read for downstream usage (likely `_members.ListInactiveMembershipsForEmployeeAsync` per Task 16's rename) — pass `callerEmployeeId.Value` |
| `ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ListMyObjectiveChangeRequestsQueryHandler.cs` | `var userId = _currentUser.UserId;` (27) | Re-read for downstream usage (likely filters `ObjectiveChangeRequest.RequestedById == userId` or `.ReportingManagerId == userId` — "my requests" vs. "requests I can approve") — pass `callerEmployeeId.Value` |
| `Objectives/Commands/UnachieveObjective/UnachieveObjectiveCommandHandler.cs` | `var userId = _currentUser.UserId;` (39), `if (objective.OwnerId != userId)` (53), `GetActiveAssigneeAsync(tenantId, objective.OwnerId, ct)` (58), `UpsertMembershipAsync(..., objective.OwnerId, headAssignee.Id, ...)` (71 — drop the trailing `headAssignee.Id` argument per Task 15's new 4-arg signature), `RequestedById = userId` (88), `ReportingManagerId = objective.ReportingManagerId!.Value` (89, unchanged — already correct once Task 13 lands) | `if (objective.OwnerId != callerEmployeeId.Value)`; `GetActiveAssigneeAsync(tenantId, objective.OwnerId, ct)` (unchanged call shape, now Employee-typed input); `UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt)`; `RequestedById = callerEmployeeId.Value` |
| `Objectives/Commands/DeleteObjective/DeleteObjectiveCommandHandler.cs` | `var userId = _currentUser.UserId;` (35), `if (objective.OwnerId != userId)` (47), `RequestedById = userId` (72), `ReportingManagerId = objective.ReportingManagerId!.Value` (73, unchanged) | Same substitutions as above |
| `Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs` | `var userId = _currentUser.UserId;` (38), `if (objective.OwnerId != userId)` (54), `RequestedById = userId` (101), `ReportingManagerId = objective.ReportingManagerId!.Value` (104, unchanged) | Same substitutions |
| `Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs` | `var userId = _currentUser.UserId;` (38), `if (objective.OwnerId != userId)` (53), `DeactivateMembershipAsync(..., objective.OwnerId, ...)` (76), `HasOtherActiveAccessAsync(..., objective.OwnerId, ...)` (77), `RequestedById = userId` (94), `ReportingManagerId = objective.ReportingManagerId!.Value` (95, unchanged) | Same substitutions; the two membership calls' argument shapes are already Task-15-compatible (they already pass a single `objective.OwnerId` as the identity argument, now Employee-typed) |
| `Projects/Mappers/ProjectMapper.cs` | `project.LeadId` passed through in 3 places (15, 38, 49) | No change — already an opaque pass-through Guid, same as noted in Task 17 |

- [ ] **Step 1: Work through the table top to bottom**

For each row: open the file, apply the resolver-injection pattern plus the listed line change, re-read any "re-read to find downstream usage" note fully before editing (those five rows were not fully captured during this plan's scoping grep and need a fresh read — don't guess at the line content).

- [ ] **Step 2: Update every corresponding test file**

Same mechanical pattern as Tasks 18–22's test updates, applied per file.

- [ ] **Step 3: Build and run the entire Work Management test slice**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: 0 build errors, 0 test failures — this is the point where Task 16's "will not fully pass yet" note from Step 4 finally resolves.

- [ ] **Step 4: Commit**

Commit each file (or small logical groups, e.g. all five "Project" handlers together, all six "Objective" handlers together) separately rather than one giant commit, matching this plan's existing one-concern-per-commit style:

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject/ src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject/ src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject/ src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/ src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectLogo/ src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects/ tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EditProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetProjectLogoQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/ListProjectsQueryHandlerTests.cs
git commit -m "feat(work): Project-level handlers - LeadId comparisons use EmployeeId"

git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/ src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/ src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/ src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory/ src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyObjectiveHistoryQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/ListMyObjectiveChangeRequestsQueryHandlerTests.cs
git commit -m "feat(work): Objective/ObjectiveChangeRequest query handlers - EmployeeId-based access and name lookups"

git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective/ src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective/ src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/ src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/ tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveObjectiveCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EditObjectiveCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveObjectiveCommandHandlerTests.cs
git commit -m "feat(work): remaining Objective lifecycle commands - OwnerId comparisons use EmployeeId"
```

---

## Task 24: Seeders — `WorkManagementSampleDataSeeder` and `WorkManagementDapiDemoSeeder`

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs`

**Interfaces:** none new — both seeders already have the `Employee` object (or an `employeeIdByPersonKey` dictionary) in scope wherever they currently set a `UserId`-valued field.

- [ ] **Step 1: `WorkManagementSampleDataSeeder.cs` — three call sites in `EnsureUserSampleProjectsAsync`**

```csharp
// Line ~205: Project.LeadId
LeadId = user.Id,
// becomes:
LeadId = employee.Id,
```

```csharp
// Line ~224: default Objective.OwnerId
OwnerId = user.Id,
// becomes:
OwnerId = employee.Id,
```

```csharp
// Line ~241: creatorMembership - drop UserId (column no longer exists per Task 13), keep EmployeeId
UserId = user.Id,
EmployeeId = employee.Id,
// becomes:
EmployeeId = employee.Id,
```

```csharp
// Line ~292: milestone Objective.OwnerId
OwnerId = user.Id,
// becomes:
OwnerId = employee.Id,
```

```csharp
// Line ~310: milestoneMembership - same UserId drop as above
UserId = user.Id,
EmployeeId = employee.Id,
// becomes:
EmployeeId = employee.Id,
```

(`Project.CreatedById`, `Objective.CreatedById`, `ProjectMember.CreatedById`, `ReleaseCalendarEntry.RecipientUserId` — all left as `user.Id`, per the Phase 2 scope boundary: these are audit/notification fields, not ownership.)

- [ ] **Step 2: `WorkManagementDapiDemoSeeder.Objectives.cs` — `SeedObjectiveNodeAsync` and `SeedProjectMemberAsync`**

```csharp
// SeedObjectiveNodeAsync currently does:
var ownerUserId = ResolveUserId(node.OwnerKey);
// ...
OwnerId = ownerUserId,
ReportingManagerId = DapiOwnerUserId,

// becomes:
var ownerEmployeeId = employeeIdByPersonKey[node.OwnerKey];
// ...
OwnerId = ownerEmployeeId,
ReportingManagerId = employeeIdByPersonKey["dabi"],
```

(`employeeIdByPersonKey` is already a parameter of `SeedObjectiveNodeAsync` — no new parameter threading needed. `ResolveUserId(node.OwnerKey)` becomes dead code in this method specifically, but is still used elsewhere in the same file for `Project.LeadId`, `AuditLog`-style fields, etc. — do not delete the method itself, only stop calling it here.)

```csharp
// Project.LeadId, ~line 39:
LeadId = DapiOwnerUserId,
// becomes:
LeadId = employeeIdByPersonKey["dabi"],
```

(This requires threading `employeeIdByPersonKey` into `SeedProjectsAndObjectivesAsync`'s project-construction block — it's already a parameter of that method, just not currently used for `LeadId`.)

```csharp
// SeedProjectMemberAsync currently does:
var userId = ResolveUserId(personKey);
var employeeId = employeeIdByPersonKey[personKey];

db.ProjectMembers.Add(new ProjectMember
{
    // ...
    UserId = userId,
    EmployeeId = employeeId,
    // ...
});

// becomes:
var employeeId = employeeIdByPersonKey[personKey];

db.ProjectMembers.Add(new ProjectMember
{
    // ...
    EmployeeId = employeeId,
    // ...
});
```

(`ResolveUserId(personKey)`/`var userId` line is deleted entirely from `SeedProjectMemberAsync` — nothing else in that method used it.)

- [ ] **Step 3: Run both seeders against a clean dev database and verify**

Run: `dotnet run --project src/ONEVO.Api` against a freshly-migrated (Task 13 applied) empty dev database, let both seeders run on startup, then:

```sql
-- Confirm every seeded Objective.OwnerId and Project.LeadId is a real Employee.Id, never a User.Id.
SELECT o.id, o.owner_id FROM objectives o
WHERE NOT EXISTS (SELECT 1 FROM employees e WHERE e.id = o.owner_id);
-- Expected: 0 rows

SELECT p.id, p.lead_id FROM projects p
WHERE NOT EXISTS (SELECT 1 FROM employees e WHERE e.id = p.lead_id);
-- Expected: 0 rows
```

- [ ] **Step 4: Run the seeder test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagementDapiDemoSeederTests|FullyQualifiedName~WorkManagementSampleDataSeederDapiGuardTests"`
Expected: PASS, including the new assertion added in Task 13 Step 5.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs
git commit -m "feat(work): dev/demo seeders write EmployeeId into OwnerId/LeadId/ReportingManagerId, drop ProjectMember.UserId"
```

---

## Task 25: Contract/Postman updates for the breaking identity change

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ListProjectsRequest.cs` (or wherever `TargetUserId` lives — rename to `TargetEmployeeId`)
- Modify: every Postman doc under `docs/postman-request/Work Management/` that documents `ownerId`, `reportingManagerId`, `leadId`, `requestedById`, `decidedById`, or `targetUserId`/`targetEmployeeId`
- Modify: `docs/postman-request/Work Management/List Projects.md` specifically (field rename)

- [ ] **Step 1: Rename `TargetUserId` → `TargetEmployeeId` on the List Projects contract**

Find the request DTO (`grep -rn "TargetUserId" src/ONEVO.Api/Contracts/WorkManagement`), rename the property, and update `ListProjectsQueryHandler`/`ListProjectsQuery` to match (already covered in Task 23's sweep table row for this file — this step is the API-contract half of that same change).

- [ ] **Step 2: Add one line to every affected Postman doc's Response section**

For every doc documenting a response field that used to carry a `users.id` value and now carries an `employees.id` value (`ownerId`, `reportingManagerId`, `leadId`, `requestedById`, `decidedById` — cross-check the full list against Task 23's table and every DTO touched in Tasks 17–21), add a short note directly under that field's description:

> **Breaking change (2026-08-14):** this field's value changed from a User id to an Employee id. The field name is unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

- [ ] **Step 3: Cross-check against the actual running code**

Same standard as the existing Task 12 Step 3 — re-open each handler alongside its doc and confirm every field/type claim matches the code as it exists after Tasks 13–24, not what this plan predicted it would look like.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/ "docs/postman-request/Work Management/"
git commit -m "docs: document the UserId to EmployeeId identity change across Work Management API responses"
```

---

## Final check before handoff

- [ ] Run the full Work Management test slice one more time: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"` — expect 0 failures.
- [ ] Run `dotnet build src/ONEVO.Api` one more time from a clean state — expect 0 errors, 0 new warnings introduced by this plan's files.
- [ ] Confirm no file outside the Global Constraints scope list was touched: `git diff --stat e1bbf99..HEAD` (or the appropriate base commit) and manually check every path against the scope guardrail.
- [ ] **Phase 2 only:** re-run the Task 13 Step 4 verification query against the dev database one final time — expect 0 rows across all three `UNION ALL` branches.
- [ ] **Phase 2 only:** `grep -rn "_currentUser.UserId" src/ONEVO.Application/Features/WorkManagement` and manually confirm every remaining hit is either (a) feeding `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` itself, or (b) an explicitly out-of-scope audit/notification field per the Phase 2 preamble (`CreatedById`, `AuditLog.UserId`, `ReleaseCalendarEntry.RecipientUserId`) — any other hit means Task 23's sweep missed a file.
- [ ] **Phase 2 only:** `grep -rln "UserId" src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs` — expect no matches (both `UserId`/`InvitedUserId` properties fully removed by Task 13).
- [ ] **Phase 2 only:** confirm with the user whether Core HR's `employees.user_id` unique constraint should be revisited now that Work Management is fully Employee-identity-based — this phase deliberately left it in place (see preamble), so multi-legal-entity-per-user is only "ready" on the Work Management side, not yet actually usable end-to-end until that separate, out-of-scope change lands.
