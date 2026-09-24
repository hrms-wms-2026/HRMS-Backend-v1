using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
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
/// the comments stack built in Tasks 1-13 actually works: post, reply, react, edit, delete and
/// their access-control rules. Mirrors the provisioning fixture pattern already established by
/// TaskAttachmentsIntegrationTests.cs.
/// </summary>
public sealed class TaskCommentsIntegrationTestsFixture : IAsyncLifetime
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
    public Guid TenantAProjectId { get; private set; }
    public Guid TenantAEmployeeId { get; private set; }
    public Guid TaskCategoryId { get; private set; }

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

        TenantA = await ProvisionAndLoginOwnerAsync("tc-int-a", "Task Comments Int A Co", "owner-a@tc-int.test");
        TenantB = await ProvisionAndLoginOwnerAsync("tc-int-b", "Task Comments Int B Co", "owner-b@tc-int.test");

        var categoryId = await SeedProjectCategoryAsync(TenantA.TenantId, "General");
        TenantAEmployeeId = await SeedEmployeeForOwnerAsync(TenantA.TenantId, "owner-a@tc-int.test");
        await SeedEmployeeForOwnerAsync(TenantB.TenantId, "owner-b@tc-int.test");

        var projectResponse = await SendCreateProjectAsync(TenantA, categoryId, "Task Comments Project", "TCI1");
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created, await projectResponse.Content.ReadAsStringAsync());
        var projectJson = await ReadJsonAsync(projectResponse);
        TenantAProjectId = projectJson.GetProperty("project").GetProperty("id").GetGuid();
        TenantAObjectiveId = projectJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        TaskCategoryId = await SeedTaskCategoryAsync(TenantA.TenantId, TenantAProjectId);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    // ── Task seeding ─────────────────────────────────────────────────────────

    public async Task<Guid> SeedTaskAsync(TenantSession? session = null, string title = "Comment target task")
    {
        session ??= TenantA;
        var createResponse = await SendCreateTaskAsync(session, TenantAObjectiveId, TaskCategoryId, title);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var created = await ReadJsonAsync(createResponse);
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Seeds a comment directly via the DbContext, authored by a synthetic employee id
    /// that is never the tenant owner's own employee id. Used to exercise author-only edit/delete
    /// enforcement without a second full login session — no fixture in this codebase already
    /// provisions a second same-tenant member session, and building one (invite + accept-password
    /// + session-exchange) is out of proportion to what this one access-control assertion needs:
    /// the handler only compares comment.EmployeeId to the caller's own resolved employee id.</summary>
    public async Task<Guid> SeedCommentAuthoredByOtherEmployeeAsync(Guid tenantId, Guid taskId, string content)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var comment = new ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskComment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(),
            Content = content, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        db.Set<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskComment>().Add(comment);
        await db.SaveChangesAsync();
        return comment.Id;
    }

    private async Task<HttpResponseMessage> SendCreateTaskAsync(
        TenantSession session, Guid objectiveId, Guid categoryId, string title)
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
            attachmentFileIds = Array.Empty<Guid>()
        };

        return await SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/objectives/{objectiveId}/tasks", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    // ── Comment HTTP helpers ─────────────────────────────────────────────────

    public Task<HttpResponseMessage> SendPostCommentAsync(
        TenantSession session, Guid taskId, string content, IReadOnlyList<Guid>? attachmentFileIds = null)
        => SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/tasks/{taskId}/comments",
            new { content, attachmentFileIds = attachmentFileIds ?? Array.Empty<Guid>() },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public async Task<Guid> PostCommentAsync(TenantSession session, Guid taskId, string content)
    {
        var response = await SendPostCommentAsync(session, taskId, content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    public Task<HttpResponseMessage> SendPostReplyAsync(
        TenantSession session, Guid commentId, string content, IReadOnlyList<Guid>? attachmentFileIds = null)
        => SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/comments/{commentId}/replies",
            new { content, attachmentFileIds = attachmentFileIds ?? Array.Empty<Guid>() },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public async Task<Guid> PostReplyAsync(TenantSession session, Guid commentId, string content)
    {
        var response = await SendPostReplyAsync(session, commentId, content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    public Task<HttpResponseMessage> SendEditCommentAsync(TenantSession session, Guid commentId, string content)
        => SendJsonAsync(HttpMethod.Patch, session.Host, $"/api/v1/work/comments/{commentId}",
            new { content, attachmentFileIds = Array.Empty<Guid>() },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public Task<HttpResponseMessage> SendDeleteCommentAsync(TenantSession session, Guid commentId)
        => SendJsonAsync(HttpMethod.Delete, session.Host, $"/api/v1/work/comments/{commentId}", null,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public Task<HttpResponseMessage> SendAddReactionAsync(TenantSession session, Guid commentId, string emoji)
        => SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/comments/{commentId}/reactions",
            new { emoji }, cookie: session.SessionCookie, csrfToken: session.CsrfHeader);

    public Task<HttpResponseMessage> SendGetCommentsAsync(TenantSession session, Guid taskId)
        => _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/tasks/{taskId}/comments"));

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

    public Task<HttpResponseMessage> SendGetTaskFileAsync(TenantSession session, Guid fileId)
        => _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/tasks/files/{fileId}"));

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
                db.RolePermissions.Add(new ONEVO.Domain.Features.Auth.Entities.RolePermission { TenantId = tenantId, RoleId = ownerRole.Id, PermissionId = permission.Id });
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
                var planReady = await db.Set<SubscriptionPlan>().AnyAsync(p => p.Id == SeededPlanId);
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
public sealed class TaskCommentsIntegrationTests : IClassFixture<TaskCommentsIntegrationTestsFixture>
{
    private readonly TaskCommentsIntegrationTestsFixture _fixture;

    public TaskCommentsIntegrationTests(TaskCommentsIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostComment_ThenGetComments_ReturnsIt()
    {
        var taskId = await _fixture.SeedTaskAsync();

        var postResponse = await _fixture.SendPostCommentAsync(_fixture.TenantA, taskId, "First comment");
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK, await postResponse.Content.ReadAsStringAsync());

        var getResponse = await _fixture.SendGetCommentsAsync(_fixture.TenantA, taskId);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());
        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain("First comment");
    }

    [Fact]
    public async Task ReplyToReply_ReturnsBadRequest()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var topLevelId = await _fixture.PostCommentAsync(_fixture.TenantA, taskId, "root");
        var replyId = await _fixture.PostReplyAsync(_fixture.TenantA, topLevelId, "first reply");

        var response = await _fixture.SendPostReplyAsync(_fixture.TenantA, replyId, "nested reply");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditComment_AsNonAuthor_ReturnsForbidden()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var commentId = await _fixture.SeedCommentAuthoredByOtherEmployeeAsync(_fixture.TenantA.TenantId, taskId, "original");

        var response = await _fixture.SendEditCommentAsync(_fixture.TenantA, commentId, "hijacked");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteComment_WithReplies_TombstonesButKeepsReplies()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var topLevelId = await _fixture.PostCommentAsync(_fixture.TenantA, taskId, "root");
        await _fixture.PostReplyAsync(_fixture.TenantA, topLevelId, "a reply");

        var deleteResponse = await _fixture.SendDeleteCommentAsync(_fixture.TenantA, topLevelId);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _fixture.SendGetCommentsAsync(_fixture.TenantA, taskId);
        var getBody = await getResponse.Content.ReadAsStringAsync();
        var comments = JsonDocument.Parse(getBody).RootElement.Clone();
        comments.EnumerateArray().Should().ContainSingle(c => c.GetProperty("id").GetGuid() == topLevelId, getBody);
        var topLevel = comments.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == topLevelId);
        topLevel.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        topLevel.GetProperty("content").GetString().Should().BeEmpty();
        topLevel.GetProperty("replies").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task AddReaction_ThenGetComments_ShowsGroupedReaction()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var commentId = await _fixture.PostCommentAsync(_fixture.TenantA, taskId, "react to me");

        var addResponse = await _fixture.SendAddReactionAsync(_fixture.TenantA, commentId, "👍");
        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _fixture.SendGetCommentsAsync(_fixture.TenantA, taskId);
        var comments = await TaskCommentsIntegrationTestsFixture.ReadJsonAsync(getResponse);
        var comment = comments.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == commentId);
        var reactions = comment.GetProperty("reactions").EnumerateArray().ToList();
        reactions.Should().ContainSingle(r => r.GetProperty("emoji").GetString() == "👍"
            && r.GetProperty("employeeIds").GetArrayLength() == 1);
    }

    [Fact]
    public async Task AddReaction_DifferentEmojiBySameEmployee_ReplacesPreviousReaction()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var commentId = await _fixture.PostCommentAsync(_fixture.TenantA, taskId, "only one reaction each");

        (await _fixture.SendAddReactionAsync(_fixture.TenantA, commentId, "👍")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fixture.SendAddReactionAsync(_fixture.TenantA, commentId, "🎉")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _fixture.SendGetCommentsAsync(_fixture.TenantA, taskId);
        var comments = await TaskCommentsIntegrationTestsFixture.ReadJsonAsync(getResponse);
        var comment = comments.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == commentId);
        var reactions = comment.GetProperty("reactions").EnumerateArray().ToList();
        reactions.Should().ContainSingle();
        reactions[0].GetProperty("emoji").GetString().Should().Be("🎉");
        reactions[0].GetProperty("reactors").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task CommentAttachment_UploadedThenLinked_ServedThroughGetTaskFile()
    {
        var taskId = await _fixture.SeedTaskAsync();
        var fileBytes = System.Text.Encoding.UTF8.GetBytes("comment attachment bytes");
        var uploadResponse = await _fixture.SendPendingUploadAsync(_fixture.TenantA, "comment_attachment", "note.pdf", "application/pdf", fileBytes);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created, await uploadResponse.Content.ReadAsStringAsync());
        var fileId = (await TaskCommentsIntegrationTestsFixture.ReadJsonAsync(uploadResponse)).GetProperty("fileId").GetGuid();

        var postResponse = await _fixture.SendPostCommentAsync(_fixture.TenantA, taskId, "see attached", new[] { fileId });
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK, await postResponse.Content.ReadAsStringAsync());

        var fileResponse = await _fixture.SendGetTaskFileAsync(_fixture.TenantA, fileId);
        fileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetComments_WithoutTaskVisibility_ReturnsNotFound()
    {
        var taskId = await _fixture.SeedTaskAsync();

        var response = await _fixture.SendGetCommentsAsync(_fixture.TenantB, taskId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
