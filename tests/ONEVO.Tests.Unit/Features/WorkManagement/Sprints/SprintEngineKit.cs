using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.DeleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>
/// Sprint handlers wired to the REAL WorkApprovalEngine (repositories mocked), so the creator-position
/// rule is exercised end to end. Tree: Root (RootOwner) → A (OwnerA) → B (OwnerB); Member is a plain
/// member of B; Outsider is a project member with no Module membership.
/// </summary>
public sealed class SprintEngineKit
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly Guid ProjectId = Guid.NewGuid();
    public static readonly Guid OwnerA = Guid.NewGuid();
    public static readonly Guid OwnerB = Guid.NewGuid();
    public static readonly Guid Member = Guid.NewGuid();
    public static readonly Guid Outsider = Guid.NewGuid();
    public static readonly Guid NonMember = Guid.NewGuid();

    private readonly Dictionary<Guid, Guid> _userToEmployee = new();
    private Guid _callerUser;

    public SprintEngineKit()
    {
        W = new SprintTestWiring(TenantId, ProjectId);
        A = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = W.Root.Id, OwnerId = OwnerA, IsActive = true, Title = "A" };
        B = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = A.Id, OwnerId = OwnerB, IsActive = true, Title = "B" };
        W.Modules.Add(A);
        W.Modules.Add(B);

        foreach (var employee in new[] { W.RootOwner, OwnerA, OwnerB, Member, Outsider, NonMember })
        {
            var user = Guid.NewGuid();
            _userToEmployee[user] = employee;
            W.Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, user, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
            W.Members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, employee, It.IsAny<CancellationToken>()))
                .ReturnsAsync(employee != NonMember);
        }
        W.Members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, Member, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { B.Id });

        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        CurrentUser.SetupGet(x => x.UserId).Returns(() => _callerUser);

        W.Projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", IsActive = true });
        W.Assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty));
        W.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask>());
        W.Sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => Sprint?.Id == id ? Sprint : null);

        // The engine asks the hierarchy for the position's active holder: the position's own owner.
        W.Hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ProjectModuleTree tree, Guid position, Guid _, CancellationToken _) => tree.Get(position)?.OwnerId);
        Requests.Setup(x => x.AddAsync(It.IsAny<WorkApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WorkApprovalRequest, CancellationToken>((r, _) => Added.Add(r)).Returns(Task.CompletedTask);
        Permissions.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
    }

    public SprintTestWiring W { get; }
    public Objective A { get; }
    public Objective B { get; }
    public Sprint? Sprint { get; private set; }
    public Mock<ICurrentUser> CurrentUser { get; } = new();
    public Mock<IWorkApprovalRequestRepository> Requests { get; } = new();
    public Mock<IPermissionResolver> Permissions { get; } = new();
    public Mock<IEmployeeAuthorityResolver> Authority { get; } = new();
    public List<WorkApprovalRequest> Added { get; } = new();

    public Guid UserOf(Guid employeeId) => _userToEmployee.First(p => p.Value == employeeId).Key;

    public void CallAs(Guid employeeId) => _callerUser = UserOf(employeeId);

    /// <summary>A sprint created by <paramref name="creator"/> whose creator position is <paramref name="position"/>.</summary>
    public Sprint GivenSprint(string status, Guid creator, Guid position)
    {
        Sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S1", Status = status,
            StartDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 1),
            EndDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 14),
            CreatedById = UserOf(creator), CreatorPositionObjectiveId = position, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        return Sprint;
    }

    private SprintActionSubmitter Submitter()
    {
        var engine = new WorkApprovalEngine(W.Hierarchy.Object, Requests.Object, W.WorkNotifications.Object, W.Projects.Object, Authority.Object);
        return new SprintActionSubmitter(W.Hierarchy.Object, W.Members.Object, W.Identity.Object, W.UnitOfWork.Object, engine, W.WorkNotifications.Object);
    }

    public CreateSprintCommandHandler Create()
        => new(CurrentUser.Object, W.Identity.Object, W.Projects.Object, W.Members.Object, Permissions.Object, W.Writes(), Submitter());

    public EditSprintCommandHandler Edit()
        => new(CurrentUser.Object, W.Identity.Object, W.Sprints.Object, W.Members.Object, W.Writes(), Submitter());

    public StartSprintCommandHandler Start()
        => new(CurrentUser.Object, W.Identity.Object, W.Sprints.Object, W.Members.Object, W.Writes(), Submitter());

    public CompleteSprintCommandHandler Complete()
        => new(CurrentUser.Object, W.Identity.Object, W.Sprints.Object, W.Members.Object, W.Writes(), Submitter());

    public AchieveSprintCommandHandler Achieve()
        => new(CurrentUser.Object, W.Identity.Object, W.Sprints.Object, W.Members.Object, W.Writes(), Submitter());

    public DeleteSprintCommandHandler Delete()
        => new(CurrentUser.Object, W.Identity.Object, W.Sprints.Object, W.Members.Object, W.Writes(), Submitter());
}
