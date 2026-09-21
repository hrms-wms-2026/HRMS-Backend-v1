using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>
/// End-to-end proof, through real HTTP calls against a real PostgreSQL database (no mocks), that
/// the task attachment stack built in Tasks 1-10 actually works together: pending-upload create,
/// linking on task create, exposure on GetTaskById, and download via GetTaskFile - including the
/// cross-tenant/cross-user access checks. Mirrors the provisioning fixture pattern in
/// Features/WorkManagement/CreateProjectEndpointTests.cs (real signup + owner-invite-accept +
/// session-exchange flow, work_management permission grant patched onto the Owner role).
/// </summary>
public sealed class TaskAttachmentsIntegrationTestsFixture : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public HttpClient Client => _client;

    public TenantSession TenantA { get; private set; } = null!;
    public TenantSession TenantB { get; private set; } = null!;
    public Guid TenantAObjectiveId { get; private set; }

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        }
        else
        {
            await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        }
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();
        await GrantCoreHrStorageAllowanceAsync();

        var loginResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        TenantA = await ProvisionAndLoginOwnerAsync("ta-int-a", "Task Attach Int A Co", "owner-a@ta-int.test");
        TenantB = await ProvisionAndLoginOwnerAsync("ta-int-b", "Task Attach Int B Co", "owner-b@ta-int.test");

        var categoryId = await SeedProjectCategoryAsync(TenantA.TenantId, "General");
        TenantAEmployeeId = await SeedEmployeeForOwnerAsync(TenantA.TenantId, "owner-a@ta-int.test");
        await SeedEmployeeForOwnerAsync(TenantB.TenantId, "owner-b@ta-int.test");

        var projectResponse = await SendCreateProjectAsync(TenantA, categoryId, "Task Attachment Project", "TAI1");
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created, await projectResponse.Content.ReadAsStringAsync());
        var projectJson = await ReadJsonAsync(projectResponse);
        TenantAProjectId = projectJson.GetProperty("project").GetProperty("id").GetGuid();
        TenantAObjectiveId = projectJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var taskCategoryId = await SeedTaskCategoryAsync(TenantA.TenantId, TenantAProjectId);
        TaskCategoryId = taskCategoryId;
    }

    public Guid TaskCategoryId { get; private set; }
    public Guid TenantAProjectId { get; private set; }
    public Guid TenantAEmployeeId { get; private set; }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    // ── Task attachment HTTP helpers ─────────────────────────────────────────

    public async Task<HttpResponseMessage> SendPendingUploadAsync(
        TenantSession session, string purpose, string fileName, string contentType, byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(purpose), "Purpose" },
            { content, "File", fileName }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/work/tasks/pending-uploads")
        {
            Content = form
        };
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);

        return await _client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendCreateTaskAsync(
        TenantSession session, Guid objectiveId, Guid categoryId, string title, IReadOnlyList<Guid>? attachmentFileIds = null)
    {
        var body = new
        {
            title,
            description = (string?)null,
            categoryId,
            priority = "medium",
            dueDate = (DateOnly?)null,
            estimatedHours = (decimal?)null,
            storyPoints = (int?)null,
            sprintId = (Guid?)null,
            attachmentFileIds
        };

        return await SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/objectives/{objectiveId}/tasks", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    public async Task<HttpResponseMessage> SendGetTaskAsync(TenantSession session, Guid taskId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/tasks/{taskId}"));

    public Task<HttpResponseMessage> SendCreateSubtaskAsync(TenantSession session, Guid parentTaskId, Guid? assigneeEmployeeId = null)
        => SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/tasks/{parentTaskId}/subtasks",
            new { title = "Integrated child", priority = "high", dueDate = (DateOnly?)null, assigneeEmployeeId },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public async Task<HttpResponseMessage> SendGetSubtasksAsync(TenantSession session, Guid parentTaskId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/tasks/{parentTaskId}/subtasks"));

    public async Task<HttpResponseMessage> SendGetProjectTasksAsync(TenantSession session, Guid projectId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/projects/{projectId}/tasks"));

    public async Task<HttpResponseMessage> SendGetTaskFileAsync(TenantSession session, Guid fileId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/tasks/files/{fileId}"));

    public HttpRequestMessage BuildGetRequest(TenantSession session, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return request;
    }

    private async Task<HttpResponseMessage> SendCreateProjectAsync(
        TenantSession session, Guid categoryId, string name, string identifier)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(categoryId.ToString()), "CategoryId" },
            { new StringContent(name), "Name" },
            { new StringContent(identifier), "Identifier" },
            { new StringContent("2026-01-01"), "StartDate" },
            { new StringContent("2026-06-01"), "TargetDate" },
            { new StringContent("2026-06-15"), "ReleaseDate" },
            { new StringContent("40"), "DefaultObjectiveAllocatedHours" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/work/projects")
        {
            Content = form
        };
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await _client.SendAsync(request);
    }

    private async Task<Guid> SeedProjectCategoryAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
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
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Dev",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Set<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCategory>().Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> SeedEmployeeForOwnerAsync(Guid tenantId, string ownerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == ownerEmail);

        var employee = new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            EmployeeNumber = "OWNER-1",
            FirstName = "Test",
            LastName = "Owner",
            Email = ownerEmail,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EmploymentStatusId = ONEVO.Domain.Lookups.EmploymentStatusIds.Active,
            CreatedById = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>
    /// No module_catalog row carries a real storage_reference in the production seed data (every
    /// module starts at "[]"), and no platform default is configured, so any tenant is
    /// "storage_not_entitled" by default. Mirrors LegalEntitiesIntegrationTests'
    /// GrantCoreHrStorageAllowanceAsync workaround: grant "core_hr" (present on every seeded
    /// tenant's subscription) a real allowance purely so the pending-upload tests below can
    /// exercise the real quota path. Test-only; no production code or seed data is touched.
    /// </summary>
    private async Task GrantCoreHrStorageAllowanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coreHr = await db.Set<ModuleCatalogItem>().SingleAsync(m => m.ModuleKey == "core_hr");
        coreHr.IsStorageConsuming = true;
        coreHr.StorageReference = """[{"min_employees":1,"max_employees":100,"storage_gb":50}]""";
        await db.SaveChangesAsync();
    }

    private async Task GrantWorkManagementAccessToOwnerRoleAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerRole = await db.Roles.SingleAsync(r => r.TenantId == tenantId && r.Name == "Owner");

        var workManagementPermissions = await db.Permissions.Where(p => p.Module == "work_management").ToListAsync();
        var alreadyGrantedIds = (await db.RolePermissions
                .Where(rp => rp.RoleId == ownerRole.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync())
            .ToHashSet();

        foreach (var permission in workManagementPermissions)
        {
            if (!alreadyGrantedIds.Contains(permission.Id))
                db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = ownerRole.Id, PermissionId = permission.Id });
        }

        var subscription = await db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();
        var modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson) ?? [];
        if (!modules.Contains("work_management"))
        {
            modules.Add("work_management");
            subscription.SelectedModulesJson = JsonSerializer.Serialize(modules);
        }

        await db.SaveChangesAsync();
    }

    public sealed record TenantSession(Guid TenantId, string Host, string SessionCookie, string CsrfHeader);

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
            subscription = new
            {
                plan_id = SeededPlanId,
                billing_cycle = "monthly",
                commercial_model = "standard"
            },
            owner_invite = new
            {
                email = ownerEmail,
                first_name = "Test",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        await GrantWorkManagementAccessToOwnerRoleAsync(tenantId);

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendJsonAsync(HttpMethod.Post, host,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
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

        var confirmResponse = await SendJsonAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        const string baseHost = "localhost";
        var loginResponse = await SendJsonAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email = ownerEmail, password = ownerPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginJson = await ReadJsonAsync(loginResponse);
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendJsonAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);

        return new TenantSession(tenantId, host, sessionCookie, csrfHeader);
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId != "tenant_owner_invite")
                    continue;
                if (!string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (template.Data.TryGetProperty("invite_token", out var token))
                    return token.GetString();
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<ONEVO.Domain.Features.Auth.Entities.Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
                    .AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady)
                    return;
            }
            catch
            {
                // Schema not created yet; keep polling.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

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

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TaskAttachmentsIntegrationTests : IClassFixture<TaskAttachmentsIntegrationTestsFixture>
{
    private readonly TaskAttachmentsIntegrationTestsFixture _fixture;

    public TaskAttachmentsIntegrationTests(TaskAttachmentsIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateSubtask_ThenFetchBoardAndChildren_PersistsRelationshipAssignmentAndCounts()
    {
        var parentResponse = await _fixture.SendCreateTaskAsync(
            _fixture.TenantA, _fixture.TenantAObjectiveId, _fixture.TaskCategoryId, "Integrated parent");
        parentResponse.StatusCode.Should().Be(HttpStatusCode.Created, await parentResponse.Content.ReadAsStringAsync());
        var parent = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(parentResponse);
        var parentTaskId = parent.GetProperty("id").GetGuid();

        var createChildResponse = await _fixture.SendCreateSubtaskAsync(
            _fixture.TenantA, parentTaskId, _fixture.TenantAEmployeeId);
        createChildResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createChildResponse.Content.ReadAsStringAsync());
        var child = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(createChildResponse);
        child.GetProperty("parentTaskId").GetGuid().Should().Be(parentTaskId);
        child.GetProperty("assigneeEmployeeIds").EnumerateArray().Single().GetGuid().Should().Be(_fixture.TenantAEmployeeId);

        var boardResponse = await _fixture.SendGetProjectTasksAsync(_fixture.TenantA, _fixture.TenantAProjectId);
        boardResponse.StatusCode.Should().Be(HttpStatusCode.OK, await boardResponse.Content.ReadAsStringAsync());
        var board = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(boardResponse);
        var boardParent = board.EnumerateArray().Single(task => task.GetProperty("id").GetGuid() == parentTaskId);
        boardParent.GetProperty("subtaskTotalCount").GetInt32().Should().Be(1);
        boardParent.GetProperty("subtaskCompletedCount").GetInt32().Should().Be(0);
        board.EnumerateArray().Should().NotContain(task => task.GetProperty("id").GetGuid() == child.GetProperty("id").GetGuid());

        var childrenResponse = await _fixture.SendGetSubtasksAsync(_fixture.TenantA, parentTaskId);
        childrenResponse.StatusCode.Should().Be(HttpStatusCode.OK, await childrenResponse.Content.ReadAsStringAsync());
        var children = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(childrenResponse);
        children.EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(child.GetProperty("id").GetGuid());

        var crossTenantResponse = await _fixture.SendGetSubtasksAsync(_fixture.TenantB, parentTaskId);
        crossTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PendingUpload_ThenCreateTaskWithAttachment_ThenGetById_ShowsAttachment_ThenFileIsDownloadable()
    {
        var fileBytes = Encoding.UTF8.GetBytes("hello attachment world");
        var uploadResponse = await _fixture.SendPendingUploadAsync(
            _fixture.TenantA, "task_attachment", "notes.pdf", "application/pdf", fileBytes);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created, await uploadResponse.Content.ReadAsStringAsync());
        var uploadJson = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(uploadResponse);
        var fileId = uploadJson.GetProperty("fileId").GetGuid();

        var createTaskResponse = await _fixture.SendCreateTaskAsync(
            _fixture.TenantA, _fixture.TenantAObjectiveId, _fixture.TaskCategoryId, "Task with attachment", new[] { fileId });
        createTaskResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createTaskResponse.Content.ReadAsStringAsync());
        var taskJson = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(createTaskResponse);
        var taskId = taskJson.GetProperty("id").GetGuid();

        var getTaskResponse = await _fixture.SendGetTaskAsync(_fixture.TenantA, taskId);
        getTaskResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getTaskResponse.Content.ReadAsStringAsync());
        var getTaskJson = await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(getTaskResponse);
        var attachments = getTaskJson.GetProperty("attachments").EnumerateArray().ToList();
        attachments.Should().ContainSingle(a => a.GetProperty("fileName").GetString() == "notes.pdf");

        var getFileResponse = await _fixture.SendGetTaskFileAsync(_fixture.TenantA, fileId);
        getFileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedBytes = await getFileResponse.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(fileBytes);
    }

    [Fact]
    public async Task GetFile_UnlinkedFileNotOwnedByCaller_Returns404()
    {
        var fileBytes = Encoding.UTF8.GetBytes("owned by tenant A's owner only");
        var uploadResponse = await _fixture.SendPendingUploadAsync(
            _fixture.TenantA, "task_attachment", "private.pdf", "application/pdf", fileBytes);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileId = (await TaskAttachmentsIntegrationTestsFixture.ReadJsonAsync(uploadResponse)).GetProperty("fileId").GetGuid();

        var getFileResponse = await _fixture.SendGetTaskFileAsync(_fixture.TenantB, fileId);

        getFileResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
