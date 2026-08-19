using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Offboarding;

public sealed class OffboardingExecutionIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_offboarding_execution_test")
        .WithUsername("test").WithPassword("test").Build();

    private readonly SystemDateTimeProvider _clock = new();
    private readonly AesEncryptionService _encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _departmentId;
    private Guid _hrAdminUserId;
    private Guid _approverUserId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(_connectionString);

        await using var db = CreateContext();
        await EnsureLookupsAsync(db);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Offboarding Execution Tenant",
            Slug = "offboarding-execution",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Acme Co",
            CountryCode = "US", CurrencyCode = "USD",
        };
        _legalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);

        var department = new Department
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId,
            Name = "Engineering", Code = "ENG", IsActive = true,
        };
        _departmentId = department.Id;
        db.Departments.Add(department);

        _hrAdminUserId = Guid.NewGuid();
        _approverUserId = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = _hrAdminUserId, TenantId = _tenantId, Email = "hr@offboarding-execution.onevo.dev", PasswordHash = "not-a-real-hash", FirstName = "HR", LastName = "Admin", IsActive = true },
            new User { Id = _approverUserId, TenantId = _tenantId, Email = "approver@offboarding-execution.onevo.dev", PasswordHash = "not-a-real-hash", FirstName = "Bypass", LastName = "Approver", IsActive = true });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task FullHappyPath_StartToComplete_LocksEmployeeRecord()
    {
        await using var db = CreateContext();
        var employeeUserId = Guid.NewGuid();
        var employee = await SeedEmployeeAsync(db, employeeUserId, "EMP-OB-HAPPY");
        db.Sessions.Add(new Session
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = employeeUserId, IsRevoked = false,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), KeyHash = Guid.NewGuid().ToString("N"),
        });
        var template = SeedOffboardingTemplate(db, "Return laptop", isBypassable: false);
        await db.SaveChangesAsync();

        var hr = HrUser();
        (await StartHandler(db, hr).Handle(
            new StartOffboardingCommand(employee.Id, "resignation", new DateOnly(2026, 12, 1), "low", null, null),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await SelectHandler(db, hr).Handle(
            new SelectOffboardingChecklistCommand(employee.Id, template.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var task = await db.EmployeeChecklistTasks.SingleAsync(t => t.EmployeeId == employee.Id);
        (await CompleteTaskHandler(db, hr).Handle(
            new CompleteEmployeeChecklistTaskCommand(employee.Id, task.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await CompleteExitHandler(db, hr).Handle(
            new CompleteOffboardingCommand(employee.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var completedEmployee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        completedEmployee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Resigned);
        completedEmployee.TerminationDate.Should().Be(new DateOnly(2026, 12, 1));

        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == employeeUserId)).IsActive.Should().BeFalse();
        (await db.Sessions.Where(s => s.UserId == employeeUserId).AllAsync(s => s.IsRevoked)).Should().BeTrue();
        (await db.OffboardingRecords.AsNoTracking().SingleAsync(r => r.EmployeeId == employee.Id))
            .Status.Should().Be(OffboardingRecordStatuses.Completed);
    }

    [Fact]
    public async Task CancelThenRestart_SecondAttemptsTasksDoNotIncludeFirstAttemptsTasks()
    {
        await using var db = CreateContext();
        var employee = await SeedEmployeeAsync(db, Guid.NewGuid(), "EMP-OB-CANCEL");
        var firstTemplate = SeedOffboardingTemplate(db, "First attempt laptop return", isBypassable: false);
        var secondTemplate = SeedOffboardingTemplate(db, "Second attempt exit interview", isBypassable: false);
        await db.SaveChangesAsync();

        var hr = HrUser();
        (await StartHandler(db, hr).Handle(
            new StartOffboardingCommand(employee.Id, "resignation", new DateOnly(2026, 12, 1), "low", null, null),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await SelectHandler(db, hr).Handle(
            new SelectOffboardingChecklistCommand(employee.Id, firstTemplate.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var firstRecordId = (await db.OffboardingRecords.SingleAsync(r => r.EmployeeId == employee.Id)).Id;
        (await CancelHandler(db, hr).Handle(new CancelOffboardingCommand(employee.Id), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        (await StartHandler(db, hr).Handle(
            new StartOffboardingCommand(employee.Id, "termination", new DateOnly(2026, 12, 15), "medium", null, null),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await SelectHandler(db, hr).Handle(
            new SelectOffboardingChecklistCommand(employee.Id, secondTemplate.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var secondRecordId = (await db.OffboardingRecords.SingleAsync(r =>
            r.EmployeeId == employee.Id && r.Status == OffboardingRecordStatuses.InProgress)).Id;
        secondRecordId.Should().NotBe(firstRecordId);

        var taskRepository = new EfEmployeeChecklistTaskRepository(db);
        var firstTasks = await taskRepository.ListByOffboardingRecordAsync(_tenantId, firstRecordId);
        var secondTasks = await taskRepository.ListByOffboardingRecordAsync(_tenantId, secondRecordId);

        firstTasks.Should().ContainSingle(t => t.TaskTitle == "First attempt laptop return");
        secondTasks.Should().ContainSingle(t => t.TaskTitle == "Second attempt exit interview");
        secondTasks.Should().NotContain(t => t.OffboardingRecordId == firstRecordId);
    }

    [Fact]
    public async Task BypassRequest_RejectThenComplete_TaskReturnsToPriorStatusAndCanStillBeCompleted()
    {
        await using var db = CreateContext();
        var employee = await SeedEmployeeAsync(db, Guid.NewGuid(), "EMP-OB-BYPASS");
        var template = SeedOffboardingTemplate(db, "Exit interview", isBypassable: true);
        await db.SaveChangesAsync();

        var hr = HrUser();
        (await StartHandler(db, hr).Handle(
            new StartOffboardingCommand(employee.Id, "resignation", new DateOnly(2026, 12, 1), "low", null, null),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await SelectHandler(db, hr).Handle(
            new SelectOffboardingChecklistCommand(employee.Id, template.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var task = await db.EmployeeChecklistTasks.SingleAsync(t => t.EmployeeId == employee.Id);
        var createResult = await CreateBypassHandler(db, hr).Handle(
            new CreateBypassRequestCommand(employee.Id, task.Id, _approverUserId, "Manager already covered this.", null),
            CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();

        var approver = new StubCurrentUser(_tenantId, _approverUserId);
        (await new RejectBypassRequestCommandHandler(
            new EfOffboardingTaskBypassRequestRepository(db),
            new EfEmployeeChecklistTaskRepository(db),
            approver,
            _clock).Handle(new RejectBypassRequestCommand(createResult.Value, "Not approved."), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var restored = await db.EmployeeChecklistTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        restored.Status.Should().Be(EmployeeChecklistTaskStatuses.Pending);

        (await CompleteTaskHandler(db, hr).Handle(
            new CompleteEmployeeChecklistTaskCommand(employee.Id, task.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await db.EmployeeChecklistTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.Should().Be(EmployeeChecklistTaskStatuses.Completed);
    }

    [Fact]
    public async Task ChangePosition_AfterOffboardingCompletion_Returns409()
    {
        await using var db = CreateContext();
        var employeeUserId = Guid.NewGuid();
        var employee = await SeedEmployeeAsync(db, employeeUserId, "EMP-OB-LOCK");
        var template = SeedOffboardingTemplate(db, "Return badge", isBypassable: false);
        await db.SaveChangesAsync();

        var hr = HrUser();
        (await StartHandler(db, hr).Handle(
            new StartOffboardingCommand(employee.Id, "termination", new DateOnly(2026, 12, 1), "high", null, null),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await SelectHandler(db, hr).Handle(
            new SelectOffboardingChecklistCommand(employee.Id, template.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        var task = await db.EmployeeChecklistTasks.SingleAsync(t => t.EmployeeId == employee.Id);
        (await CompleteTaskHandler(db, hr).Handle(
            new CompleteEmployeeChecklistTaskCommand(employee.Id, task.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await CompleteExitHandler(db, hr).Handle(
            new CompleteOffboardingCommand(employee.Id),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var employees = new EfEmployeeRepository(db);
        var result = await new ChangeEmployeePositionCommandHandler(
            employees,
            new EfPositionRepository(db),
            new EfPositionAssignmentRepository(db),
            new UnitOfWork(db),
            hr,
            new EfAuthRepository(db),
            new EfAccessGrantRequestRepository(db),
            _clock,
            new OutboxWriter(db, _encryption, _clock),
            new EfAuthRepository(db),
            new EfTenantRepository(db),
            new EmployeeOffboardingLockGuard(employees)).Handle(
            new ChangeEmployeePositionCommand(employee.Id, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "LateralMove"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    private StubCurrentUser HrUser() => new(_tenantId, _hrAdminUserId);

    private async Task<EmployeeEntity> SeedEmployeeAsync(ApplicationDbContext db, Guid userId, string employeeNumber)
    {
        db.Users.Add(new User
        {
            Id = userId, TenantId = _tenantId, Email = $"{employeeNumber.ToLowerInvariant()}@offboarding-execution.onevo.dev",
            PasswordHash = "not-a-real-hash", FirstName = "Leaving", LastName = "Employee", IsActive = true,
        });
        var employee = new EmployeeEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = userId, EmployeeNumber = employeeNumber,
            FirstName = "Leaving", LastName = "Employee",
            Email = $"{employeeNumber.ToLowerInvariant()}@offboarding-execution.onevo.dev",
            LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
            EmploymentStatusId = EmploymentStatusIds.Active, EmploymentTypeId = 1, WorkModeId = 1,
            HireDate = new DateOnly(2024, 1, 1),
        };
        db.Employees.Add(employee);
        return employee;
    }

    private Domain.Features.CoreHr.Entities.ChecklistTemplate SeedOffboardingTemplate(ApplicationDbContext db, string title, bool isBypassable)
    {
        var bypassJson = isBypassable ? ",\"isBypassable\":true" : string.Empty;
        var template = new Domain.Features.CoreHr.Entities.ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = title, TemplateType = "offboarding",
            LegalEntityId = _legalEntityId,
            TasksJson = "[{\"title\":\"" + title + "\",\"ownerType\":\"employee\",\"dueOffsetDays\":0,\"isRequired\":true" + bypassJson + "}]",
            IsActive = true,
        };
        db.ChecklistTemplates.Add(template);
        return template;
    }

    private static async Task EnsureLookupsAsync(ApplicationDbContext db)
    {
        if (!await db.EmploymentStatuses.AnyAsync(x => x.Id == EmploymentStatusIds.Active))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Active, Code = "active", Label = "Active" });
        if (!await db.EmploymentStatuses.AnyAsync(x => x.Id == EmploymentStatusIds.Terminated))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Terminated, Code = "terminated", Label = "Terminated" });
        if (!await db.EmploymentStatuses.AnyAsync(x => x.Id == EmploymentStatusIds.Offboarding))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Offboarding, Code = "offboarding", Label = "Offboarding" });
        if (!await db.EmploymentStatuses.AnyAsync(x => x.Id == EmploymentStatusIds.Resigned))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Resigned, Code = "resigned", Label = "Resigned" });
        if (!await db.EmploymentTypes.AnyAsync(x => x.Id == 1))
            db.EmploymentTypes.Add(new EmploymentType { Id = 1, Code = "full_time", Label = "Full-Time" });
        if (!await db.WorkModes.AnyAsync(x => x.Id == 1))
            db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });
        await db.SaveChangesAsync();
    }

    private StartOffboardingCommandHandler StartHandler(ApplicationDbContext db, ICurrentUser user) =>
        new(new EfEmployeeRepository(db), new EfOffboardingRecordRepository(db), AllowAllCoverage, user, _clock);

    private SelectOffboardingChecklistCommandHandler SelectHandler(ApplicationDbContext db, ICurrentUser user) =>
        new(new EfOffboardingRecordRepository(db), new EfChecklistTemplateRepository(db),
            new EfEmployeeChecklistTaskRepository(db), new EfEmployeeRepository(db), AllowAllCoverage, user, _clock);

    private CancelOffboardingCommandHandler CancelHandler(ApplicationDbContext db, ICurrentUser user) =>
        new(new EfOffboardingRecordRepository(db), new EfEmployeeRepository(db), AllowAllCoverage, user, _clock);

    private CompleteEmployeeChecklistTaskCommandHandler CompleteTaskHandler(ApplicationDbContext db, ICurrentUser user) =>
        new(new EfEmployeeChecklistTaskRepository(db), new EfOffboardingTaskBypassRequestRepository(db), user, _clock);

    private CreateBypassRequestCommandHandler CreateBypassHandler(ApplicationDbContext db, ICurrentUser user) =>
        new(new EfEmployeeChecklistTaskRepository(db), new EfOffboardingTaskBypassRequestRepository(db),
            new EfOffboardingRecordRepository(db), new UnitOfWork(db), user, _clock);

    private CompleteOffboardingCommandHandler CompleteExitHandler(ApplicationDbContext db, ICurrentUser user)
    {
        var auth = new EfAuthRepository(db);
        return new CompleteOffboardingCommandHandler(
            new EfOffboardingRecordRepository(db), new EfEmployeeChecklistTaskRepository(db),
            new EfEmployeeRepository(db), auth, auth, new UnitOfWork(db), AllowAllCoverage, user, _clock);
    }

    private static readonly IEmployeeOffboardingCoverageGuard AllowAllCoverage = new AllowAllCoverageGuard();

    private sealed class AllowAllCoverageGuard : IEmployeeOffboardingCoverageGuard
    {
        public Task<ONEVO.Application.Common.Models.Result?> EnsureCovered(
            Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default)
            => Task.FromResult<ONEVO.Application.Common.Models.Result?>(null);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "hr@offboarding-execution.onevo.dev";
        public IReadOnlyList<string> Permissions { get; } = ["employees:read", "employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
