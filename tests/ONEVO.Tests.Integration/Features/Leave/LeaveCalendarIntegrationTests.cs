using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.Leave;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class LeaveCalendarIntegrationTests : IAsyncLifetime
{
    private const string TenantHost = "acme.localhost";
    private const string HrManagerEmail = "paramanathanmuthaiya@gmail.com";
    private const string SmokeUserPassword = "Password123!";
    private const string FixtureCodePrefix = "CALSMOKE";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private TenantSession _hrManager = null!;
    private CalendarFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_leave_calendar_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();
        _hrManager = await LoginViaBaseHostAsync(TenantHost, HrManagerEmail, SmokeUserPassword);
        _fixture = await SeedCalendarFixtureAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Calendar_ReturnsApprovedBlocksAndHonorsTentativeToggle()
    {
        var withoutTentative = await SendAsync(
            HttpMethod.Get,
            _hrManager.Host,
            "/api/v1/leave/calendar?year=2026&month=8&includeTentative=false",
            body: null,
            cookie: _hrManager.SessionCookie,
            csrfToken: _hrManager.CsrfHeader);
        var withoutTentativeJson = await ReadJsonAsync(withoutTentative);
        withoutTentative.StatusCode.Should().Be(HttpStatusCode.OK, withoutTentativeJson.ToString());

        withoutTentativeJson.GetProperty("days").EnumerateArray().Should().HaveCount(31);
        var approvedDay = FindDay(withoutTentativeJson, "2026-08-10");
        approvedDay.GetProperty("absences").EnumerateArray().Should().ContainSingle(absence =>
            absence.GetProperty("leaveTypeCode").GetString() == _fixture.LeaveTypeCode &&
            absence.GetProperty("isTentative").GetBoolean() == false);

        var pendingDayWhenDisabled = FindDay(withoutTentativeJson, "2026-08-11");
        pendingDayWhenDisabled.GetProperty("absences").EnumerateArray().Should().NotContain(absence =>
            absence.GetProperty("leaveTypeCode").GetString() == _fixture.LeaveTypeCode);

        var withTentative = await SendAsync(
            HttpMethod.Get,
            _hrManager.Host,
            "/api/v1/leave/calendar?year=2026&month=8&includeTentative=true",
            body: null,
            cookie: _hrManager.SessionCookie,
            csrfToken: _hrManager.CsrfHeader);
        var withTentativeJson = await ReadJsonAsync(withTentative);
        withTentative.StatusCode.Should().Be(HttpStatusCode.OK, withTentativeJson.ToString());

        var pendingDayWhenEnabled = FindDay(withTentativeJson, "2026-08-11");
        pendingDayWhenEnabled.GetProperty("absences").EnumerateArray().Should().ContainSingle(absence =>
            absence.GetProperty("leaveTypeCode").GetString() == _fixture.LeaveTypeCode &&
            absence.GetProperty("isTentative").GetBoolean());
    }

    private async Task<CalendarFixture> SeedCalendarFixtureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "acme");
        var employee = await db.Employees.SingleAsync(e => e.TenantId == tenant.Id && e.Email == HrManagerEmail);

        var existingTypeIds = await db.LeaveTypes
            .Where(t => t.TenantId == tenant.Id && t.Code.StartsWith(FixtureCodePrefix))
            .Select(t => t.Id)
            .ToListAsync();
        if (existingTypeIds.Count > 0)
        {
            db.LeaveRequests.RemoveRange(db.LeaveRequests.Where(r => r.TenantId == tenant.Id && existingTypeIds.Contains(r.LeaveTypeId)));
            db.LeaveTypes.RemoveRange(db.LeaveTypes.Where(t => existingTypeIds.Contains(t.Id)));
            await db.SaveChangesAsync();
        }

        var leaveTypeId = Guid.NewGuid();
        var leaveTypeCode = $"{FixtureCodePrefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveTypeId,
            TenantId = tenant.Id,
            Name = "Calendar Smoke Leave",
            Code = leaveTypeCode,
            Category = LeaveTypeCategories.Annual,
            IsPaid = true,
            RequiresApproval = true,
            DefaultDaysPerYear = 10m,
            ApplicableGender = LeaveGenderRestrictions.All,
            CreatedAt = now
        });

        db.LeaveRequests.AddRange(
            new LeaveRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EmployeeId = employee.Id,
                LeaveTypeId = leaveTypeId,
                StartDate = new DateOnly(2026, 8, 10),
                EndDate = new DateOnly(2026, 8, 10),
                TotalDays = 1m,
                PaidDays = 1m,
                Status = LeaveRequestStatuses.Approved,
                ApprovedBy = employee.UserId,
                ApprovedAt = now,
                CreatedAt = now
            },
            new LeaveRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EmployeeId = employee.Id,
                LeaveTypeId = leaveTypeId,
                StartDate = new DateOnly(2026, 8, 11),
                EndDate = new DateOnly(2026, 8, 11),
                TotalDays = 1m,
                PaidDays = 1m,
                Status = LeaveRequestStatuses.Pending,
                CreatedAt = now
            },
            new LeaveRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EmployeeId = employee.Id,
                LeaveTypeId = leaveTypeId,
                StartDate = new DateOnly(2026, 8, 12),
                EndDate = new DateOnly(2026, 8, 12),
                TotalDays = 1m,
                PaidDays = 1m,
                Status = LeaveRequestStatuses.Rejected,
                CreatedAt = now
            });

        await db.SaveChangesAsync();

        return new CalendarFixture(leaveTypeCode);
    }

    private async Task<TenantSession> LoginViaBaseHostAsync(string host, string email, string password)
    {
        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email, password });
        var loginJson = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        var exchangeJson = await ReadJsonAsync(exchangeResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, exchangeJson.ToString());
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);
        return new TenantSession(host, sessionCookie, csrfHeader);
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
                var permissionsReady = await db.Permissions.AnyAsync();
                var planReady = await db.Set<SubscriptionPlan>().AnyAsync(p => p.Id == SeededPlanId);
                var smokeTenantReady = await db.Set<Tenant>().AnyAsync(t => t.Slug == "acme") &&
                                       await db.Users.AnyAsync(u => u.Email == HrManagerEmail) &&
                                       await db.Employees.AnyAsync(e => e.Email == HrManagerEmail);
                if (permissionsReady && planReady && smokeTenantReady)
                    return;
            }
            catch
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan / acme smoke tenant missing).");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static JsonElement FindDay(JsonElement json, string date)
    {
        return json.GetProperty("days").EnumerateArray()
            .Single(day => day.GetProperty("date").GetString() == date);
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

    private sealed record CalendarFixture(string LeaveTypeCode);
    private sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);
}
