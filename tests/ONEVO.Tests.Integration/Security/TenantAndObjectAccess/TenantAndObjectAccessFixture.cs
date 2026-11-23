using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Security.TenantAndObjectAccess;

/// <summary>
/// Task 27 (docs/Improvements/person-1-security-and-auth): two real tenants provisioned through the
/// platform admin API and signed in through the real login + session-exchange flow, on SQL Server
/// with row-level security on. Tenant B's owner owns one object in every module; tenant A has its
/// owner plus four plain users - an employee, a colleague, a manager of another team (holds the
/// read permissions but covers nobody) and HR - so the tests can try every object id both across
/// the tenant line and across colleagues inside one tenant.
/// Every name this fixture writes for tenant B or the colleague carries a marker string, so a test
/// can also prove that no part of the victim's data reached the response body.
/// </summary>
public sealed class TenantAndObjectAccessFixture : IAsyncLifetime
{
    public const string TenantBMarker = "zzTenantBSecret";
    public const string ColleagueMarker = "zzColleagueSecret";

    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    /// <summary>A real 1x1 PNG, so image checks on logo and banner uploads accept it.</summary>
    public static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly byte[] PdfBytes = "%PDF-1.4 cross tenant probe"u8.ToArray();

    private static readonly string[] RequiredPermissionCodes =
        ["employees:write", "attendance:read", "leave:approve", "monitoring:read", "leave:read", "leave:manage"];

    private readonly CapturingEmailService _email = new();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public TenantSession OwnerA { get; private set; } = null!;
    public TenantSession OwnerB { get; private set; } = null!;
    public TenantSession Employee { get; private set; } = null!;
    public TenantSession Colleague { get; private set; } = null!;
    public TenantSession OtherTeamManager { get; private set; } = null!;
    public TenantSession Hr { get; private set; } = null!;
    public TenantSession CoveringManager { get; private set; } = null!;

    public TenantObjects A { get; private set; } = null!;
    public TenantObjects B { get; private set; } = null!;

    public Guid EmployeeEmployeeId { get; private set; }
    public Guid ColleagueEmployeeId { get; private set; }
    public Guid ManagerEmployeeId { get; private set; }
    public Guid ColleaguePendingFileId { get; private set; }
    public Guid ColleagueDashboardId { get; private set; }
    public Guid ColleagueLeaveRequestId { get; private set; }
    public Guid ColleagueScreenshotId { get; private set; }
    public Guid ColleagueDeviceId { get; private set; }

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROWNUS_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        else
            await AdminTestFactory.MigrateDatabaseAsync(connectionString);

        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();
        await GrantCoreHrStorageAllowanceAsync();

        var adminSession = await PlatformAdminTestSignIn.SignInAsync(
            _client, "test_admin@onevo.dev", "test_password_123", AdminHost);
        _adminCsrfToken = adminSession.CsrfToken;
        _adminCookie = adminSession.SessionCookie;

        OwnerA = await ProvisionAndLoginOwnerAsync("xt-acc-a", "Access Probe A Co", "owner-a@xt-acc.test");
        OwnerB = await ProvisionAndLoginOwnerAsync("xt-acc-b", $"Access Probe B Co {TenantBMarker}", "owner-b@xt-acc.test");

        var legalEntityA = await GetPrimaryLegalEntityIdAsync(OwnerA.TenantId);
        var legalEntityB = await GetPrimaryLegalEntityIdAsync(OwnerB.TenantId);
        var ownerEmployeeA = await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "owner-a@xt-acc.test", "OWN-A", "Owner");
        var ownerEmployeeB = await AddEmployeeForUserAsync(OwnerB.TenantId, legalEntityB, "owner-b@xt-acc.test", "OWN-B", TenantBMarker);

        // Tenant A's plain users. None holds coverage over anyone, so the directory and the
        // per-employee overviews must refuse every colleague's id - including for the manager.
        Employee = await SeedAndLoginUserAsync(OwnerA, "employee@xt-acc-a.test", [], "Plain Employee");
        Colleague = await SeedAndLoginUserAsync(OwnerA, "colleague@xt-acc-a.test", [], "Plain Colleague");
        OtherTeamManager = await SeedAndLoginUserAsync(OwnerA, "manager@xt-acc-a.test",
            ["attendance:read", "leave:approve", "monitoring:read", "monitoring:screenshots:request", "employees:write"], "Other Team Manager");
        Hr = await SeedAndLoginUserAsync(OwnerA, "hr@xt-acc-a.test",
            ["leave:read", "leave:manage", "attendance:read"], "HR");
        // Positive control for the monitoring probes: same monitoring permissions as the other-team
        // manager, plus a position with company-wide coverage of tenant A's legal entity.
        CoveringManager = await SeedAndLoginUserAsync(OwnerA, "covering@xt-acc-a.test",
            ["monitoring:read", "monitoring:screenshots:request"], "Covering Manager");

        EmployeeEmployeeId = await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "employee@xt-acc-a.test", "EMP-E", "Employee");
        ColleagueEmployeeId = await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "colleague@xt-acc-a.test", "EMP-C", ColleagueMarker);
        ManagerEmployeeId = await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "manager@xt-acc-a.test", "EMP-M", "Manager");
        await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "hr@xt-acc-a.test", "EMP-H", "Hr");
        var coveringEmployeeId = await AddEmployeeForUserAsync(OwnerA.TenantId, legalEntityA, "covering@xt-acc-a.test", "EMP-CM", "Covering");
        await GrantCompanyCoverageAsync(OwnerA.TenantId, legalEntityA, coveringEmployeeId);

        A = await CreateObjectsAsync(OwnerA, legalEntityA, ownerEmployeeA, "Alpha", "XTA");
        B = await CreateObjectsAsync(OwnerB, legalEntityB, ownerEmployeeB, TenantBMarker, "XTB");

        // The colleague's own objects inside tenant A.
        ColleaguePendingFileId = await UploadFileAsync(Colleague, "generic_document", $"{ColleagueMarker}.pdf", "application/pdf", PdfBytes);
        ColleagueDashboardId = await GetMyDashboardIdAsync(Colleague);
        ColleagueLeaveRequestId = await SeedLeaveRequestAsync(OwnerA.TenantId, ColleagueEmployeeId, A.LeaveTypeId, ColleagueMarker);
        (ColleagueScreenshotId, ColleagueDeviceId) = await SeedColleagueMonitoringAsync(OwnerA.TenantId, legalEntityA);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    // ── Objects ──────────────────────────────────────────────────────────────

    /// <summary>One object per module, owned by the tenant's owner.</summary>
    public sealed record TenantObjects(
        Guid TenantId,
        Guid LegalEntityId,
        Guid OwnerEmployeeId,
        Guid OwnerRoleId,
        Guid DepartmentId,
        Guid LeaveTypeId,
        Guid LeaveRequestId,
        Guid ProjectId,
        Guid ObjectiveId,
        Guid TaskId,
        Guid TaskFileId,
        Guid PendingFileId,
        Guid LogoFileId,
        Guid SupportTicketId,
        Guid DashboardId,
        Guid ConversationId);

    private async Task<TenantObjects> CreateObjectsAsync(
        TenantSession owner, Guid legalEntityId, Guid ownerEmployeeId, string label, string code)
    {
        var departmentResponse = await SendJsonAsync(owner, HttpMethod.Post,
            $"/api/v1/org/legal-entities/{legalEntityId}/departments", new { name = $"Dept {label}" });
        departmentResponse.StatusCode.Should().Be(HttpStatusCode.Created, await departmentResponse.Content.ReadAsStringAsync());
        var departmentId = (await ReadJsonAsync(departmentResponse)).GetProperty("id").GetGuid();

        var leaveTypeResponse = await SendJsonAsync(owner, HttpMethod.Post, "/api/v1/leave/types", LeaveTypeBody($"Leave {label}", code));
        leaveTypeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await leaveTypeResponse.Content.ReadAsStringAsync());
        var leaveTypeId = (await ReadJsonAsync(leaveTypeResponse)).GetProperty("id").GetGuid();
        var policyResponse = await SendJsonAsync(owner, HttpMethod.Post, "/api/v1/leave/policies", LeavePolicyBody($"Policy {label}", leaveTypeId, legalEntityId));
        policyResponse.StatusCode.Should().Be(HttpStatusCode.OK, await policyResponse.Content.ReadAsStringAsync());
        var leaveRequestId = await SeedLeaveRequestAsync(owner.TenantId, ownerEmployeeId, leaveTypeId, label);

        var categoryId = await SeedProjectCategoryAsync(owner.TenantId);
        var projectResponse = await SendCreateProjectAsync(owner, categoryId, $"Project {label}", code);
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created, await projectResponse.Content.ReadAsStringAsync());
        var projectJson = await ReadJsonAsync(projectResponse);
        var projectId = projectJson.GetProperty("project").GetProperty("id").GetGuid();
        var objectiveId = projectJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var taskCategoryId = await SeedTaskCategoryAsync(owner.TenantId, projectId);

        var taskFileId = await UploadTaskPendingFileAsync(owner, $"{label}.pdf");
        var taskResponse = await SendJsonAsync(owner, HttpMethod.Post, $"/api/v1/work/objectives/{objectiveId}/tasks", new
        {
            title = $"Task {label}",
            description = (string?)null,
            categoryId = taskCategoryId,
            priority = "medium",
            attachmentFileIds = new[] { taskFileId }
        });
        taskResponse.StatusCode.Should().Be(HttpStatusCode.Created, await taskResponse.Content.ReadAsStringAsync());
        var taskId = (await ReadJsonAsync(taskResponse)).GetProperty("id").GetGuid();

        var pendingFileId = await UploadFileAsync(owner, "generic_document", $"{label}.pdf", "application/pdf", PdfBytes);

        var logoFileId = await UploadFileAsync(owner, "company_logo", "logo.png", "image/png", PngBytes);
        var logoResponse = await SendJsonAsync(owner, HttpMethod.Put, $"/api/v1/org/legal-entities/{legalEntityId}/logo", new { fileId = logoFileId });
        logoResponse.StatusCode.Should().Be(HttpStatusCode.OK, await logoResponse.Content.ReadAsStringAsync());

        var ticketResponse = await SendJsonAsync(owner, HttpMethod.Post, "/api/v1/support/tickets", new
        {
            subject = $"Ticket {label}",
            description = $"Ticket body {label}",
            priority = "medium",
            category = "general"
        });
        ticketResponse.IsSuccessStatusCode.Should().BeTrue(await ticketResponse.Content.ReadAsStringAsync());
        var ticketId = (await ReadJsonAsync(ticketResponse)).GetProperty("id").GetGuid();

        var dashboardId = await GetMyDashboardIdAsync(owner);
        var conversationId = await SeedConversationAsync(owner.TenantId, taskId, ownerEmployeeId);

        return new TenantObjects(
            owner.TenantId, legalEntityId, ownerEmployeeId, await GetOwnerRoleIdAsync(owner.TenantId),
            departmentId, leaveTypeId, leaveRequestId, projectId, objectiveId, taskId, taskFileId,
            pendingFileId, logoFileId, ticketId, dashboardId, conversationId);
    }

    public static object LeaveTypeBody(string name, string code) => new
    {
        name,
        code,
        description = "access probe type",
        color = "#4F46E5",
        isPaid = true,
        applicableGender = "all",
        requiresDocument = false,
        documentRequiredAfterHours = (decimal?)null,
        acceptedDocumentTypes = Array.Empty<string>()
    };

    /// <summary>One "any one approver" policy, so approve/cancel reach their approver and owner checks.</summary>
    private static object LeavePolicyBody(string name, Guid leaveTypeId, Guid legalEntityId) => new
    {
        leaveTypeId,
        name,
        description = "access probe policy",
        rules = new
        {
            amountHours = 160m,
            amountPeriod = "year",
            carryForwardCapHours = 40m,
            carryForwardExpiryMonths = 3,
            eligibleFrom = "immediately",
            eligibleAfterMonths = (int?)null,
            minimumNoticeDays = 0,
            minRequestHours = 4m,
            maxRequestHours = 112m,
            maxTeamAbsencePercent = 100m,
            approvalMode = "any_one",
            blackouts = Array.Empty<object>()
        },
        assignments = new[] { new { targetKind = "legal_entity", targetId = legalEntityId } },
        replaceConflicts = false
    };

    private async Task<Guid> GetMyDashboardIdAsync(TenantSession session)
    {
        var response = await SendAsync(session, HttpMethod.Get, "/api/v1/dashboards/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        return json.EnumerateArray().First().GetProperty("id").GetGuid();
    }

    public async Task<Guid> UploadFileAsync(TenantSession session, string purpose, string fileName, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(purpose), "purpose" },
            { file, "file", fileName }
        };
        var response = await SendAsync(session, HttpMethod.Post, "/api/v1/files", form);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("fileId").GetGuid();
    }

    public async Task<Guid> UploadTaskPendingFileAsync(TenantSession session, string fileName)
    {
        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("task_attachment"), "Purpose" },
            { file, "File", fileName }
        };
        var response = await SendAsync(session, HttpMethod.Post, "/api/v1/work/tasks/pending-uploads", form);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("fileId").GetGuid();
    }

    private async Task<HttpResponseMessage> SendCreateProjectAsync(TenantSession session, Guid categoryId, string name, string identifier)
    {
        var banner = new ByteArrayContent(PngBytes);
        banner.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(categoryId.ToString()), "CategoryId" },
            { new StringContent(name), "Name" },
            { new StringContent(identifier), "Identifier" },
            { new StringContent("2026-01-01"), "StartDate" },
            { new StringContent("2027-06-01"), "TargetDate" },
            { new StringContent("2027-06-15"), "ReleaseDate" },
            { new StringContent("40"), "DefaultObjectiveAllocatedHours" },
            { banner, "Banner", "banner.png" }
        };
        using var request = BuildRequest(session, HttpMethod.Post, "/api/v1/work/projects", form);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await _client.SendAsync(request);
    }

    // ── Database seeding (system context, outside any request) ───────────────

    private async Task<Guid> SeedLeaveRequestAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = DateTimeOffset.UtcNow.Date.AddDays(30);
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            StartAt = new DateTimeOffset(start.AddHours(9), TimeSpan.Zero),
            EndAt = new DateTimeOffset(start.AddHours(17), TimeSpan.Zero),
            TotalHours = 8m,
            PaidHours = 8m,
            Reason = $"Reason {label}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<Guid> SeedConversationAsync(Guid tenantId, Guid taskId, Guid ownerEmployeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectSource = "work",
            SubjectKind = "task",
            SubjectId = taskId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Conversations.Add(conversation);
        db.ConversationParticipants.Add(new ConversationParticipant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversation.Id,
            EmployeeId = ownerEmployeeId,
            AddedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    private async Task<Guid> SeedProjectCategoryAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "General",
            IsActive = true,
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ProjectCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> SeedTaskCategoryAsync(Guid tenantId, Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Dev", CreatedAt = DateTimeOffset.UtcNow
        };
        db.Set<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory>().Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> AddEmployeeForUserAsync(Guid tenantId, Guid legalEntityId, string email, string number, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == email);
        var employee = new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            EmployeeNumber = number,
            FirstName = "Probe",
            LastName = lastName,
            Email = email,
            LegalEntityId = legalEntityId,
            HireDate = new DateOnly(2024, 1, 1),
            EmploymentStatusId = ONEVO.Domain.Lookups.EmploymentStatusIds.Active,
            CreatedById = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>A position for the employee with active company-wide coverage of the legal entity.</summary>
    private async Task GrantCompanyCoverageAsync(Guid tenantId, Guid legalEntityId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var position = new Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Covering Manager",
            PositionType = Position.TypeUnique,
            MaxOccupancy = 1,
            IsActive = true,
        };
        db.Positions.Add(position);
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        db.ManagementCoverageRecords.Add(new ManagementCoverageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            OwnerPositionId = position.Id,
            CoveredTargetType = ManagementCoverageRecord.TargetCompany,
            OwnerOrder = 1,
            Source = ManagementCoverageRecord.SourceManual,
            IsLocked = false,
            Status = ManagementCoverageRecord.StatusActive,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The colleague's monitoring objects: one screenshot evidence asset (pointing at the colleague's
    /// uploaded file, so a signed URL can be issued) and one active tray device.
    /// </summary>
    private async Task<(Guid ScreenshotId, Guid DeviceId)> SeedColleagueMonitoringAsync(Guid tenantId, Guid legalEntityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var asset = new MonitoringEvidenceAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = ColleagueEmployeeId,
            FileRecordId = ColleaguePendingFileId,
            EvidenceType = "screenshot",
            Source = "agent",
            TriggerType = "periodic",
            CapturedAt = now,
            CreatedAt = now,
        };
        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Colleague.UserId,
            LegalEntityId = legalEntityId,
            DeviceName = $"{ColleagueMarker}-laptop",
            DeviceOs = "windows",
            DeviceFingerprint = $"fp-{Guid.NewGuid():N}",
            IsActive = true,
            ActivatedAt = now,
            CreatedAt = now,
        };
        db.Add(asset);
        db.Add(device);
        await db.SaveChangesAsync();
        return (asset.Id, device.Id);
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LegalEntities.Where(x => x.TenantId == tenantId && x.IsPrimary).Select(x => x.Id).SingleAsync();
    }

    private async Task<Guid> GetOwnerRoleIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Roles.Where(r => r.TenantId == tenantId && r.Name == "Owner").Select(r => r.Id).SingleAsync();
    }

    private async Task GrantCoreHrStorageAllowanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coreHr = await db.Set<ModuleCatalogItem>().SingleAsync(m => m.ModuleKey == "core_hr");
        coreHr.IsStorageConsuming = true;
        coreHr.StorageReference = """[{"min_employees":1,"max_employees":100,"storage_gb":50}]""";
        await db.SaveChangesAsync();
    }

    private async Task GrantWorkManagementToOwnerRoleAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerRole = await db.Roles.SingleAsync(r => r.TenantId == tenantId && r.Name == "Owner");
        var granted = (await db.RolePermissions.Where(rp => rp.RoleId == ownerRole.Id).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();
        foreach (var permission in await db.Permissions.Where(p => p.Module == "work_management").ToListAsync())
        {
            if (!granted.Contains(permission.Id))
                db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = ownerRole.Id, PermissionId = permission.Id });
        }

        var subscription = await db.TenantSubscriptions.Where(s => s.TenantId == tenantId).OrderByDescending(s => s.CreatedAt).FirstAsync();
        var modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson) ?? [];
        if (!modules.Contains("work_management"))
        {
            modules.Add("work_management");
            subscription.SelectedModulesJson = JsonSerializer.Serialize(modules);
        }

        await db.SaveChangesAsync();
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    public sealed record TenantSession(Guid TenantId, Guid UserId, string Host, string SessionCookie, string CsrfHeader);

    private async Task<TenantSession> SeedAndLoginUserAsync(
        TenantSession owner, string email, IReadOnlyList<string> permissionCodes, string roleName)
    {
        var tenantId = owner.TenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            db.Add(new User
            {
                Id = userId, TenantId = tenantId, Email = email, FirstName = "Probe", LastName = roleName,
                PasswordHash = hasher.Hash(FixtureUserPassword), IsActive = true, EmailVerified = true,
                MustChangePassword = false, PasswordSetByAdmin = false, CreatedAt = now, CreatedById = userId
            });

            var roleId = Guid.NewGuid();
            db.Add(new Role
            {
                Id = roleId, TenantId = tenantId, Name = roleName, Description = $"Access probe role: {roleName}",
                IsSystem = false, CreatedAt = now, CreatedById = userId
            });
            foreach (var code in permissionCodes)
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code)
                    ?? throw new InvalidOperationException($"Permission '{code}' is not seeded.");
                db.Add(new RolePermission { TenantId = tenantId, RoleId = roleId, PermissionId = permission.Id });
            }

            db.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedBy = userId });
            foreach (var (type, decision) in new[] { ("terms", "accepted"), ("privacy_notice", "acknowledged") })
            {
                db.Add(new LegalAcceptanceRecord
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, DocumentType = type, DocumentVersion = "1.0",
                    Decision = decision, Required = true, DecidedAt = now, Source = "test-seed"
                });
            }

            await db.SaveChangesAsync();
        }

        return await LoginAsync(tenantId, owner.Host, email, FixtureUserPassword);
    }

    private async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
    {
        const string ownerPassword = "OwnerPass@2026!";
        var host = $"{slug}.localhost";

        var createBody = new
        {
            company_name = companyName,
            slug,
            industry_profile = "technology",
            company_size_range = "11-50",
            legal_entity_name = companyName,
            registration_number = $"PV-{slug}",
            country = "LK",
            timezone = "Asia/Colombo",
            currency = "LKR",
            subscription = new { plan_id = SeededPlanId, billing_cycle = "monthly", commercial_model = "standard" },
            owner_invite = new
            {
                email = ownerEmail, first_name = "Test", last_name = "Owner", completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendRawJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            _adminCookie, _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        await GrantWorkManagementToOwnerRoleAsync(tenantId);

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendRawJsonAsync(HttpMethod.Post, host, $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = ownerPassword,
                confirm_password = ownerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmResponse = await SendRawJsonAsync(HttpMethod.Patch, AdminHost, $"/admin/v1/tenants/{tenantId}/provision/confirm",
            new { confirm = true }, _adminCookie, _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return await LoginAsync(tenantId, host, ownerEmail, ownerPassword);
    }

    private async Task<TenantSession> LoginAsync(Guid tenantId, string host, string email, string password)
    {
        var loginResponse = await SendRawJsonAsync(HttpMethod.Post, "localhost", "/api/v1/auth/login", new { email, password });
        var loginJson = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendRawJsonAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange", new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exchangeResponse.Content.ReadAsStringAsync());
        var cookies = ParseSetCookies(exchangeResponse);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            userId = await db.Users.Where(u => u.TenantId == tenantId && u.Email == email).Select(u => u.Id).SingleAsync();
        }

        return new TenantSession(
            tenantId,
            userId,
            host,
            $"crownus_session={cookies["crownus_session"]}; crownus_csrf={cookies["crownus_csrf"]}",
            Uri.UnescapeDataString(cookies["crownus_csrf"]));
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId == "tenant_owner_invite"
                    && string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase)
                    && template.Data.TryGetProperty("invite_token", out var token))
                {
                    return token.GetString();
                }
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            await migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var seededCodes = await db.Set<Permission>()
                    .Where(p => RequiredPermissionCodes.Contains(p.Code)).CountAsync();
                if (seededCodes == RequiredPermissionCodes.Length
                    && await db.Set<SubscriptionPlan>().AnyAsync(p => p.Id == SeededPlanId))
                {
                    return;
                }
            }
            catch
            {
                // Seeders still running.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 60s (permissions / subscription plan missing).");
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    public HttpRequestMessage BuildRequest(TenantSession session, HttpMethod method, string path, HttpContent? content = null, string? host = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Host = host ?? session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(
        TenantSession session, HttpMethod method, string path, HttpContent? content = null, string? host = null)
    {
        using var request = BuildRequest(session, method, path, content, host);
        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendJsonAsync(TenantSession session, HttpMethod method, string path, object? body, string? host = null)
        => SendAsync(session, method, path, body is null ? null : JsonContent.Create(body), host);

    public async Task<HttpResponseMessage> SendAnonymousAsync(HttpMethod method, string host, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendRawJsonAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null) request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return cookies;
        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var idx = pair.IndexOf('=');
            if (idx > 0)
                cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }
        return cookies;
    }
}
