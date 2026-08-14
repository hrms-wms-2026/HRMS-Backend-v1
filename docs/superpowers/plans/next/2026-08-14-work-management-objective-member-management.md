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

## Task 4: Add Objective Member — invite instead of direct add

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/AddObjectiveMemberOutcomeResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AddMember` action only)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (Task 3).
- Produces: `AddObjectiveMemberOutcomeResponse(bool AlreadyMember, ProjectMemberInvitationResponse? Invitation)` — read by the controller in this task only.

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
    private static readonly Guid MemberUserId = Guid.NewGuid();
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
            : assignee ?? new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = MemberUserId, EmploymentStatusId = EmploymentStatusIds.Active };
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberUserId, It.IsAny<CancellationToken>())).ReturnsAsync(mockAssignee);
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

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Equal(ProjectInvitationTypes.Member, result.Value.Invitation!.InviteType);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedUserId == MemberUserId
            && i.InviteType == ProjectInvitationTypes.Member && i.Status == ProjectInvitationStatuses.Pending), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_NoOpReturnsAlreadyMemberTrue()
    {
        var (handler, invitations, _) = BuildHandler(SubObjective(), alreadyActiveMember: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyPendingInvite_ReturnsConflict()
    {
        var existing = new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedUserId = MemberUserId, InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending };
        var (handler, _, _) = BuildHandler(SubObjective(), existingPendingInvite: existing);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), explicitNullAssignee: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

Note: `Handle_AlreadyActiveMember_NoOpReturnsAlreadyMemberTrue` calls a new `IMilestoneMembershipCoordinator.HasActiveMembershipAsync` method — add it to `IMilestoneMembershipCoordinator` and `MilestoneMembershipCoordinator` (delegating to `IProjectMemberRepository.HasActiveMembershipAsync`, which already exists) as part of Step 3 below, since the interface doesn't have it yet.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests"`
Expected: FAIL to compile — `AddObjectiveMemberCommand`/`Handler` don't have the new shape yet, `HasActiveMembershipAsync` doesn't exist on the coordinator interface.

- [ ] **Step 3: Add `HasActiveMembershipAsync` to the membership coordinator**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs
// Add this method to the existing interface:
    /// <summary>True if the user has an active membership row scoped to exactly this objective.</summary>
    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs
// Add this method to the existing class:
    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, userId, ct);
        return existing?.IsActive == true;
    }
```

- [ ] **Step 4: Rewrite the command, response DTO, and handler**

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

public sealed record AddObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result<AddObjectiveMemberOutcomeResponse>>;
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

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.UserId, ct);
        if (assignee is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("The member must be an active employee in this tenant.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.UserId, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Success(new AddObjectiveMemberOutcomeResponse(AlreadyMember: true, Invitation: null));

        if (await _invitations.GetPendingForObjectiveAndUserAsync(tenantId, objective.Id, request.UserId, ct) is not null)
            return Result<AddObjectiveMemberOutcomeResponse>.Conflict("An invitation is already pending for this user on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = objective.ProjectId,
            ObjectiveId = objective.Id,
            InvitedUserId = request.UserId,
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

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests"`
Expected: all 7 tests PASS.

- [ ] **Step 6: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing AddMember action:

    /// <summary>Invites a user to this milestone. Head-only. Immediate no-op (204) if already an active member; otherwise creates a pending invitation (202) the invited user must accept.</summary>
    [HttpPost("{id:guid}/members")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddObjectiveMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddObjectiveMemberCommand(id, request.UserId), ct);

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
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work): Add Objective Member now creates a pending invitation instead of adding directly"
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

## Task 10: Transfer Objective Head — no-Reporting-Manager branch

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveChangeOutcomeResponse.cs` → replaced by a new `TransferOutcomeResponse` used only by Transfer (do not change the existing type — Delete/Edit/Achieve/Unachieve keep using `ObjectiveChangeOutcomeResponse` unmodified)
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/TransferOutcomeResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer` action only)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository` (Task 2), `ProjectMemberInvitationMapper.ToResponse` (Task 3).
- Produces: `TransferOutcomeResponse(bool Applied, ObjectiveChangeRequestResponse? PendingChangeRequest, ProjectMemberInvitationResponse? PendingInvitation)`.

- [ ] **Step 1: Add the failing test**

Add this test to the existing `TransferObjectiveHeadCommandHandlerTests.cs` (first read that file in full to see its current `BuildHandler` shape — it will need a new `Mock<IProjectMemberInvitationRepository>` parameter threaded through the constructor call, mirroring how Task 4/5 extended their sibling test files):

```csharp
    [Fact]
    public async Task Handle_NonCreatorCaller_ObjectiveHasNoReportingManager_CreatesLeaderInvitationInsteadOfChangeRequest()
    {
        var objective = SubObjective(); // set ReportingManagerId = null and CreatedById = some OtherUserId, not HeadId
        objective.ReportingManagerId = null;
        objective.CreatedById = Guid.NewGuid(); // caller (HeadId) did not create it
        var newHeadId = Guid.NewGuid();

        var (handler, invitations, changeRequests) = BuildHandler(objective);

        var result = await handler.Handle(new TransferObjectiveHeadCommand(ObjectiveId, newHeadId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingInvitation);
        Assert.Null(result.Value.PendingChangeRequest);
        Assert.Equal(HeadId, objective.OwnerId); // caller stays Head until accepted
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedUserId == newHeadId && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
        changeRequests.Verify(x => x.AddAsync(It.IsAny<ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Wire `BuildHandler` to also construct and return a `Mock<IProjectMemberInvitationRepository>`, passed into the handler's constructor as its new final dependency, and update every existing test's assertion on `result.Value!.Applied`/`.PendingRequest` to the renamed shape: `.PendingChangeRequest` in place of `.PendingRequest` (the property is renamed, not removed, so every pre-existing assertion needs the rename, not new logic).

- [ ] **Step 2: Run tests to verify the new one fails and existing ones fail to compile**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TransferObjectiveHeadCommandHandlerTests"`
Expected: FAIL to compile — `TransferOutcomeResponse` doesn't exist yet, `PendingRequest` renamed.

- [ ] **Step 3: Add the new response type**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/TransferOutcomeResponse.cs
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record TransferOutcomeResponse(
    bool Applied, ObjectiveChangeRequestResponse? PendingChangeRequest, ProjectMemberInvitationResponse? PendingInvitation);
```

- [ ] **Step 4: Update the command's return type**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public sealed record TransferObjectiveHeadCommand(Guid ObjectiveId, Guid NewHeadUserId) : IRequest<Result<TransferOutcomeResponse>>;
```

- [ ] **Step 5: Rewrite the handler — change every `Result<ObjectiveChangeOutcomeResponse>` to `Result<TransferOutcomeResponse>`, wrap the immediate-apply return in the new shape, and insert the no-RM branch**

The full handler, with the new branch inserted between the existing creator-immediate branch and the existing RM-routing branch:

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

        if (objective.CreatedById == userId)
        {
            var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadUserId, ct);
            if (newHeadAssignee is null)
                return Result<TransferOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadUserId = objective.OwnerId;

                objective.OwnerId = request.NewHeadUserId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = request.NewHeadUserId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.NewHeadUserId, newHeadAssignee.Id, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadUserId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, request.NewHeadUserId, userId, "projects:access", innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadUserId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<TransferOutcomeResponse>.Success(new TransferOutcomeResponse(Applied: true, PendingChangeRequest: null, PendingInvitation: null));
            }, ct);
        }

        // New branch (2026-08-14): no Reporting Manager to route an approval to — send a direct,
        // no-approval invitation to the proposed new head instead. Caller remains Head until accepted.
        if (objective.ReportingManagerId is null)
        {
            var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadUserId, ct);
            if (newHeadAssignee is null)
                return Result<TransferOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

            var invitation = new ProjectMemberInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = objective.ProjectId,
                ObjectiveId = objective.Id,
                InvitedUserId = request.NewHeadUserId,
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

        var payload = new TransferObjectiveRequestPayload(request.NewHeadUserId);

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

Note the `objective.ReportingManagerId.Value` (no more `!`) on the last remaining branch — now safe both in fact and in the type checker, since the `is null` branch above already handles the null case explicitly instead of relying on an assumed invariant.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TransferObjectiveHeadCommandHandlerTests"`
Expected: all tests (existing, renamed, plus the new one) PASS.

- [ ] **Step 7: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing Transfer action:

    /// <summary>Reassigns a milestone's head. If the objective has a Reporting Manager, applies immediately for the creator or routes to that Reporting Manager for approval otherwise (unchanged). If the objective has no Reporting Manager, skips approval entirely and sends a direct invitation to the proposed new head, who must accept it - the caller remains Head until then.</summary>
    [HttpPost("{id:guid}/transfer")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadUserId), ct);

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
Expected: 0 errors. Since `ObjectiveChangeOutcomeResponse` (unchanged) is still used by Delete/Edit/Achieve/Unachieve, confirm those four actions in the same controller file still compile untouched — this task only renamed Transfer's own response type, not the shared one.

- [ ] **Step 9: Full Objectives test class + solution-wide test run**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: every Work Management test passes (this catches any other place that referenced `TransferObjectiveHeadCommandHandler`'s old response shape that wasn't in the file list above).

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs
git commit -m "feat(work): Transfer sends a direct leader invitation when the objective has no Reporting Manager"
```

---

## Task 11: Create Objective — creator always starts as owner; optional invitations

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create` action only)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs` (find this file first — it exists per the sibling-tests pattern already established for every other handler in this folder)

**Interfaces:**
- Consumes: `IProjectMemberInvitationRepository.AddAsync` (Task 2).
- Produces: nothing new — `CreateObjective`'s existing `HeadUserId` field is **repurposed**, not removed: it now means "invite this person as leader" instead of "immediately make this person the head."

**⚠️ Behavior change to flag explicitly:** today, `HeadUserId` on Create **immediately** sets `objective.OwnerId` to that user, bypassing "creator becomes owner first" entirely. This contradicts the user's stated rule (2026-08-14): the creator is always the starting owner; a leader assignment goes through accept, same as everywhere else. This task changes that existing, already-shipped behavior — call this out to the user when reporting this task's completion, not just in this plan file.

- [ ] **Step 1: Read the existing test file to see its current shape**

Open `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs` in full before writing new tests — it almost certainly has a test asserting the current "HeadUserId immediately sets OwnerId" behavior, which this task inverts. That test's name and assertion need to change, not just gain neighbors.

- [ ] **Step 2: Write/update the failing tests**

Add/replace tests in `CreateObjectiveCommandHandlerTests.cs` to cover:

```csharp
    [Fact]
    public async Task Handle_NoHeadUserIdOrInvitations_CreatorIsOwnerImmediately_NoInvitationsCreated()
    {
        var (handler, invitations, _) = BuildHandler(ParentObjective());

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadUserId: null, MemberInvitations: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CallerId, result.Value!.OwnerId); // creator is owner, always
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HeadUserIdDifferentFromCreator_CreatorStillOwnerImmediately_LeaderInvitationCreated()
    {
        var proposedHeadId = Guid.NewGuid();
        var (handler, invitations, _) = BuildHandler(ParentObjective());

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadUserId: proposedHeadId, MemberInvitations: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CallerId, result.Value!.OwnerId); // NOT proposedHeadId - creator stays owner until accepted
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.InvitedUserId == proposedHeadId && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MemberInvitationsProvided_CreatesOnePendingInvitePerEntry()
    {
        var memberOneId = Guid.NewGuid();
        var memberTwoId = Guid.NewGuid();
        var (handler, invitations, _) = BuildHandler(ParentObjective());

        var result = await handler.Handle(new CreateObjectiveCommand(
            ParentObjectiveId, "Title", null, StartDate, EndDate, 10m, HeadUserId: null,
            MemberInvitations: new List<(Guid UserId, string Type)> { (memberOneId, "member"), (memberTwoId, "member") }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedUserId == memberOneId && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedUserId == memberTwoId && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Extend `BuildHandler` in that file to also construct a `Mock<IProjectMemberInvitationRepository>`, pass it into the handler's constructor, and return it as a third tuple element — mirroring how Tasks 4/5/10 extended their own sibling test files. Its `GetActiveAssigneeAsync` mock for a proposed non-creator `HeadUserId` must still be wired (the invitation still needs a valid active-employee check before it's created), and the existing membership-upsert assertions that assumed `HeadUserId` was upserted directly must be removed — that upsert no longer happens for a non-creator `HeadUserId`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateObjectiveCommandHandlerTests"`
Expected: FAIL to compile — `CreateObjectiveCommand` doesn't have `MemberInvitations` yet, and the existing immediate-head-assignment test (if not yet updated per Step 1) will fail on the new expected behavior.

- [ ] **Step 4: Update the request contract, command, and handler**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class CreateObjectiveMemberInvitationRequest
{
    public Guid UserId { get; set; }
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
    /// <summary>If set and different from the creator, invites this person as leader (pending accept) - does not immediately assign headship. See TransferObjectiveHead's invite flow for the same acceptance mechanism.</summary>
    public Guid? HeadUserId { get; set; }
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
    Guid? HeadUserId,
    IReadOnlyList<(Guid UserId, string Type)>? MemberInvitations
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
    private readonly IPermissionAutoGrantService _autoGrant;
    private readonly IProjectMemberInvitationRepository _invitations;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant,
        IProjectMemberInvitationRepository invitations)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
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

        // Creator-employee check happens regardless of HeadUserId — the creator is always the
        // Objective's immediate owner and its first membership row.
        var creatorAssignee = await _membership.GetActiveAssigneeAsync(tenantId, userId, ct);
        if (creatorAssignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The creator must be an active employee in this tenant.");

        // A proposed non-creator leader must resolve to an active employee before anything is
        // created, same fail-fast-before-any-write shape as every other handler in this file.
        if (request.HeadUserId.HasValue && request.HeadUserId.Value != userId)
        {
            var proposedHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.HeadUserId.Value, ct);
            if (proposedHeadAssignee is null)
                return Result<ObjectiveDetailResponse>.Failure("The proposed head must be an active employee in this tenant.");
        }

        if (request.MemberInvitations is not null)
        {
            foreach (var invite in request.MemberInvitations)
            {
                var inviteeAssignee = await _membership.GetActiveAssigneeAsync(tenantId, invite.UserId, ct);
                if (inviteeAssignee is null)
                    return Result<ObjectiveDetailResponse>.Failure($"Invited member {invite.UserId} must be an active employee in this tenant.");
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
                // Creator is always the starting owner (user rule, 2026-08-14) - HeadUserId no
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

            if (request.HeadUserId.HasValue && request.HeadUserId.Value != userId)
            {
                var proposedHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.HeadUserId.Value, innerCt);
                await _invitations.AddAsync(new ProjectMemberInvitation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                    InvitedUserId = request.HeadUserId.Value, InvitedEmployeeId = proposedHeadAssignee!.Id,
                    InviteType = ProjectInvitationTypes.Leader, Status = ProjectInvitationStatuses.Pending,
                    InvitedById = userId, CreatedById = userId, CreatedAt = now
                }, innerCt);
            }

            if (request.MemberInvitations is not null)
            {
                foreach (var invite in request.MemberInvitations)
                {
                    var inviteeAssignee = await _membership.GetActiveAssigneeAsync(tenantId, invite.UserId, innerCt);
                    await _invitations.AddAsync(new ProjectMemberInvitation
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                        InvitedUserId = invite.UserId, InvitedEmployeeId = inviteeAssignee!.Id,
                        InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending,
                        InvitedById = userId, CreatedById = userId, CreatedAt = now
                    }, innerCt);
                }
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
}
```

Note: `_autoGrant` is now unused in this handler (it was only ever invoked for the immediate `resolvedHeadUserId` path, which no longer exists — a leader invite's accept step grants access instead, per Task 7). Remove the now-dead `IPermissionAutoGrantService` field/constructor parameter/using entirely rather than leaving it unused — an unused injected dependency is exactly the kind of leftover this codebase's architecture tests have caught before (see `ONEVO_Backend_Architecture_Document.md` §3.3.1 precedent). Re-check this handler's final form has no unused `using` or field before moving to Step 5.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateObjectiveCommandHandlerTests"`
Expected: all tests PASS, including every pre-existing test not touched by this task's new cases.

- [ ] **Step 6: Update the controller action**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
// Replace the existing Create action's command construction line:

        var command = new CreateObjectiveCommand(
            request.ParentObjectiveId, request.Title, request.Description,
            request.StartDate, request.EndDate, request.AllocatedHours, request.HeadUserId,
            request.MemberInvitations?.Select(m => (m.UserId, m.Type)).ToList());
```
(The rest of the `Create` action — the `result.IsSuccess ? StatusCode(201, ...) : Problem(...)` shape — is unchanged; `ObjectiveDetailResponse`'s own shape didn't change, only how `OwnerId` gets set.)

- [ ] **Step 7: Build the whole solution and run every Work Management test**

Run: `dotnet build src/ONEVO.Api && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: 0 build errors, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs
git commit -m "feat(work): Create Objective - creator is always the starting owner; HeadUserId now invites a leader instead of assigning immediately"
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

## Final check before handoff

- [ ] Run the full Work Management test slice one more time: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"` — expect 0 failures.
- [ ] Run `dotnet build src/ONEVO.Api` one more time from a clean state — expect 0 errors, 0 new warnings introduced by this plan's files.
- [ ] Confirm no file outside the Global Constraints scope list was touched: `git diff --stat e1bbf99..HEAD` (or the appropriate base commit) and manually check every path against the scope guardrail.
