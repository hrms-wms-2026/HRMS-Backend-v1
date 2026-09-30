using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>One cloned PostgreSQL database per test class, with a tenant, a legal entity, the
/// "active" employment status, and small seeding helpers shared by every My Team integration
/// test (spec §18.2).</summary>
public sealed class MyTeamDb
{
    private readonly SystemDateTimeProvider _clock = new();
    private readonly Dictionary<string, Guid> _permissionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Guid> _taskCategoryByProject = new();
    private string _connectionString = string.Empty;

    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }

    public static async Task<MyTeamDb> CreateAsync()
    {
        var helper = new MyTeamDb { _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync() };
        await using var db = helper.NewContext();
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == 1))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "My Team Tenant", Slug = $"my-team-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "51-200", Status = TenantStatus.Active,
        };
        helper.TenantId = tenant.Id;
        db.Tenants.Add(tenant);
        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "My Team Co",
            Timezone = "UTC", WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
            StandardWorkingDays = "[1,2,3,4,5,6,7]", BreakDurationMinutes = 60,
        };
        helper.LegalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);
        await db.SaveChangesAsync();
        return helper;
    }

    public ApplicationDbContext NewContext(CountingDbCommandInterceptor? counter = null)
    {
        var tenantContext = new TenantContextAccessor();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext));
        if (counter is not null)
            builder.AddInterceptors(counter);

        return new ApplicationDbContext(
            builder.Options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    public Employee AddEmployee(ApplicationDbContext db, Guid? departmentId = null, Guid? legalEntityId = null, string? lastName = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(),
            EmployeeNumber = $"MT-{Guid.NewGuid():N}"[..12], FirstName = "Team",
            LastName = lastName ?? $"Member{Guid.NewGuid():N}"[..14],
            Email = $"{Guid.NewGuid():N}@my-team-test.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
            LegalEntityId = legalEntityId ?? LegalEntityId, DepartmentId = departmentId, EmploymentStatusId = 1,
        };
        db.Employees.Add(employee);
        return employee;
    }

    public Guid AddDepartment(ApplicationDbContext db, Guid? parentId = null, bool active = true, Guid? legalEntityId = null)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = legalEntityId ?? LegalEntityId,
            Name = $"Dept {Guid.NewGuid():N}"[..14], ParentDepartmentId = parentId, IsActive = active,
        };
        db.Departments.Add(department);
        return department.Id;
    }

    public Guid AddPositionHeldBy(ApplicationDbContext db, Guid employeeId)
    {
        var position = new Position
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            Name = $"Pos {Guid.NewGuid():N}"[..13], PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true,
        };
        db.Positions.Add(position);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
        });
        return position.Id;
    }

    /// <summary>An expired/ended primary position assignment - never counted as "active" by
    /// ResolveVisibilityAsync or EmployeeVisibilityScopeResolver. Used by
    /// EmployeeVisibilityScopeMatcherParityTests to prove the matcher agrees (clarification 4).</summary>
    public Guid AddEndedPositionHeldBy(ApplicationDbContext db, Guid employeeId)
    {
        var position = new Position
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            Name = $"Pos {Guid.NewGuid():N}"[..13], PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true,
        };
        db.Positions.Add(position);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Ended,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
        });
        return position.Id;
    }

    public void AddCoverage(ApplicationDbContext db, Guid ownerPositionId, string targetType,
        Guid? coveredPositionId = null, Guid? coveredDepartmentId = null, bool active = true)
    {
        db.ManagementCoverageRecords.Add(new ManagementCoverageRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            OwnerPositionId = ownerPositionId, CoveredTargetType = targetType,
            CoveredPositionId = coveredPositionId, CoveredDepartmentId = coveredDepartmentId,
            OwnerOrder = 1, Source = ManagementCoverageRecord.SourceManual, IsLocked = false,
            Status = active ? ManagementCoverageRecord.StatusActive : ManagementCoverageRecord.StatusInactive,
        });
    }

    public void GrantPermission(ApplicationDbContext db, Guid userId, string permissionCode)
    {
        if (!_permissionIds.TryGetValue(permissionCode, out var permissionId))
        {
            permissionId = Guid.NewGuid();
            _permissionIds[permissionCode] = permissionId;
            db.Permissions.Add(new Permission { Id = permissionId, Code = permissionCode, Module = "core_hr", Description = permissionCode });
        }

        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = TenantId, Name = $"R{Guid.NewGuid():N}"[..20], CreatedById = userId });
        db.RolePermissions.Add(new RolePermission { TenantId = TenantId, RoleId = roleId, PermissionId = permissionId });
        db.UserRoles.Add(new UserRole { TenantId = TenantId, UserId = userId, RoleId = roleId, AssignedBy = userId });
    }

    public Guid AddLeaveType(ApplicationDbContext db)
    {
        var id = Guid.NewGuid();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = TenantId, Name = "Annual", Code = $"AN{Guid.NewGuid():N}"[..8],
            Category = LeaveTypeCategories.Annual, IsPaid = true, RequiresApproval = true,
            DefaultDaysPerYear = 10m, ApplicableGender = LeaveGenderRestrictions.All,
        });
        return id;
    }

    public LeaveRequest AddApprovedLeave(ApplicationDbContext db, Guid employeeId, Guid leaveTypeId, DateOnly from, DateOnly to)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            StartAt = new DateTimeOffset(from.ToDateTime(new TimeOnly(0, 0)), TimeSpan.Zero),
            EndAt = new DateTimeOffset(to.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero),
            TotalHours = 8m, PaidHours = 8m, Status = LeaveRequestStatuses.Approved,
            ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.LeaveRequests.Add(request);
        return request;
    }

    public (Guid ProjectId, Guid RootObjectiveId) AddProject(ApplicationDbContext db, Guid leadEmployeeId, bool active = true, Guid? legalEntityId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = TenantId, OwningLegalEntityId = legalEntityId ?? LegalEntityId,
            CategoryId = Guid.NewGuid(), Name = $"Project {Guid.NewGuid():N}"[..16],
            Identifier = $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(), LeadId = leadEmployeeId,
            StartDate = today.AddMonths(-1), TargetDate = today.AddMonths(3), IsActive = active,
        };
        db.Projects.Add(project);
        var root = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = project.Id, ParentObjectiveId = null, IsDefault = true,
            Title = project.Name, OwnerId = leadEmployeeId, IsActive = true,
            StartDate = project.StartDate, EndDate = project.TargetDate,
        };
        db.Objectives.Add(root);
        AddMember(db, project.Id, root.Id, leadEmployeeId);
        var category = new TaskCategory { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = project.Id, Name = "General", DisplayOrder = 0 };
        db.TaskCategories.Add(category);
        _taskCategoryByProject[project.Id] = category.Id;
        return (project.Id, root.Id);
    }

    public Guid AddModule(ApplicationDbContext db, Guid projectId, Guid parentObjectiveId, Guid ownerEmployeeId, bool achieved = false)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var module = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ParentObjectiveId = parentObjectiveId,
            Title = $"Module {Guid.NewGuid():N}"[..14], OwnerId = ownerEmployeeId, ReportingManagerId = ownerEmployeeId,
            IsActive = true, IsAchieved = achieved, StartDate = today.AddMonths(-1), EndDate = today.AddMonths(2),
        };
        db.Objectives.Add(module);
        return module.Id;
    }

    public void AddMember(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid employeeId)
        => db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ObjectiveId = objectiveId,
            EmployeeId = employeeId, MembershipSource = ProjectMembershipSources.System, IsActive = true,
        });

    public Guid AddStatus(ApplicationDbContext db, Guid projectId, bool marksComplete)
    {
        var status = new TaskStatusEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId,
            Name = marksComplete ? "Done" : "Doing", DisplayOrder = marksComplete ? 9 : 1,
            MarksTaskComplete = marksComplete,
            Category = marksComplete ? TaskStatusCategories.Done : TaskStatusCategories.Active,
        };
        db.TaskStatuses.Add(status);
        return status.Id;
    }

    public WorkTask AddTask(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid statusId, int progress, DateOnly? due,
        Guid? parentTaskId = null, Guid? assigneeEmployeeId = null)
    {
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ObjectiveId = objectiveId, ParentTaskId = parentTaskId,
            ShortId = $"T-{Guid.NewGuid():N}"[..8], Title = $"Task {Guid.NewGuid():N}"[..12],
            CategoryId = _taskCategoryByProject[projectId], StatusId = statusId, Priority = WorkTaskPriorities.Medium,
            DueDate = due, ProgressPercent = progress,
        };
        db.WorkTasks.Add(task);
        if (assigneeEmployeeId is Guid assignee)
        {
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = Guid.NewGuid(), TaskId = task.Id, EmployeeId = assignee, UserId = Guid.NewGuid(),
                AssignedById = assignee, AssignedAt = DateTimeOffset.UtcNow,
            });
        }
        return task;
    }
}
