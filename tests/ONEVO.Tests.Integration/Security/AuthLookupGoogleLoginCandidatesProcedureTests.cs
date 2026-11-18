using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Proves the pre-tenant Google lookup (DEC-062) works under RLS for the restricted app role:
/// subject links are found across tenants even when the Google email differs, an email match already
/// linked to another Google account is never offered, a tenant policy that disables Google removes
/// its users, the domain list is returned for the application check, and the restricted role still
/// cannot read user_external_identities or tenant_auth_policies directly. Requires Docker.
/// </summary>
public sealed class AuthLookupGoogleLoginCandidatesProcedureTests : IAsyncLifetime
{
    private const string Subject = "google-sub-linked";
    private const string SharedEmail = "google-lookup-shared@test.onevo.dev";

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private string _restrictedConnectionString = string.Empty;
    private Guid _linkedTenantId;
    private Guid _linkedUserId;
    private Guid _domainTenantId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        await using var db = CreateContext();

        Tenant NewTenant(string slug) => new()
        {
            Id = Guid.NewGuid(), Name = slug, Slug = slug, CompanySizeRange = "51-200", Status = TenantStatus.Active
        };
        User NewUser(Guid tenantId, string email) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = email, PasswordHash = "not-a-real-hash",
            FirstName = "Google", LastName = "Lookup", IsActive = true
        };

        var linked = NewTenant("google-lookup-linked");
        var otherSub = NewTenant("google-lookup-other-sub");
        var disabled = NewTenant("google-lookup-disabled");
        var domains = NewTenant("google-lookup-domains");
        db.Tenants.AddRange(linked, otherSub, disabled, domains);
        await db.SaveChangesAsync();

        // Linked by subject, with a company email that differs from the Google email.
        var linkedUser = NewUser(linked.Id, "someone@company.test");
        var otherSubUser = NewUser(otherSub.Id, SharedEmail);
        var disabledUser = NewUser(disabled.Id, SharedEmail);
        var domainsUser = NewUser(domains.Id, SharedEmail);
        db.Users.AddRange(linkedUser, otherSubUser, disabledUser, domainsUser);
        await db.SaveChangesAsync();

        db.UserExternalIdentities.AddRange(
            Identity(linked.Id, linkedUser.Id, Subject),
            Identity(otherSub.Id, otherSubUser.Id, "google-sub-someone-else"));
        db.TenantAuthPolicies.AddRange(
            new TenantAuthPolicy { Id = Guid.NewGuid(), TenantId = disabled.Id, GoogleCompletionAllowed = false },
            new TenantAuthPolicy
            {
                Id = Guid.NewGuid(), TenantId = domains.Id, GoogleCompletionAllowed = true,
                AllowedLoginDomainsJson = """["onevo.dev"]"""
            });
        await db.SaveChangesAsync();

        _linkedTenantId = linked.Id;
        _linkedUserId = linkedUser.Id;
        _domainTenantId = domains.Id;

        _restrictedConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            UserID = "crownus_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword
        }.ConnectionString;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RestrictedRole_SubjectLinkFound_ReturnsOnlyTheLinkedUser_EvenWithADifferentEmail()
    {
        var rows = await LookupAsync(Subject, SharedEmail);

        rows.Should().ContainSingle("email matches are only a fallback when no eligible user is linked to the subject");
        rows[0].TenantId.Should().Be(_linkedTenantId);
        rows[0].UserId.Should().Be(_linkedUserId);
        rows[0].BySubject.Should().BeTrue();
    }

    [Fact]
    public async Task RestrictedRole_NoSubjectLink_FallsBackToEligibleEmailMatchesOnly()
    {
        var rows = await LookupAsync("google-sub-never-linked", SharedEmail);

        rows.Should().ContainSingle(
            "the user linked to another Google account and the tenant that disabled Google must be excluded");
        rows[0].TenantId.Should().Be(_domainTenantId);
        rows[0].BySubject.Should().BeFalse();
        rows[0].Domains.Should().Be("""["onevo.dev"]""");
    }

    private async Task<List<(Guid TenantId, Guid UserId, bool BySubject, string? Domains)>> LookupAsync(
        string subject, string email)
    {
        await using var connection = TestDb.CreateConnection(_restrictedConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "auth_internal.auth_lookup_google_login_candidates";
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@provider_subject", subject);
        command.Parameters.AddWithValue("@normalized_email", email);

        var rows = new List<(Guid TenantId, Guid UserId, bool BySubject, string? Domains)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetGuid(reader.GetOrdinal("tenant_id")),
                    reader.GetGuid(reader.GetOrdinal("user_id")),
                    reader.GetBoolean(reader.GetOrdinal("matched_by_subject")),
                    reader.IsDBNull(reader.GetOrdinal("allowed_login_domains_json"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("allowed_login_domains_json"))));
            }
        }

        return rows;
    }

    [Theory]
    [InlineData("SELECT COUNT_BIG(*) FROM dbo.user_external_identities")]
    [InlineData("SELECT COUNT_BIG(*) FROM dbo.tenant_auth_policies")]
    public async Task RestrictedRole_CannotReadLookupTablesDirectly(string sql)
    {
        await using var connection = TestDb.CreateConnection(_restrictedConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        (await command.ExecuteScalarAsync()).Should().Be(0L,
            "without a tenant context RLS hides every row; only the lookup procedure crosses tenants");
    }

    [Fact]
    public async Task LookupOwner_CannotReadProviderEmail()
    {
        await using var connection = TestDb.CreateConnection(_connectionString);
        await connection.OpenAsync();
        await using (var impersonate = connection.CreateCommand())
        {
            impersonate.CommandText = "EXECUTE AS USER = 'crownus_auth_base_login_fn_owner'";
            await impersonate.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) provider_email FROM dbo.user_external_identities";
        var act = async () => await command.ExecuteScalarAsync();

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(230,
            "the owner may read only the columns the lookup needs");
    }

    private UserExternalIdentity Identity(Guid tenantId, Guid userId, string subject) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Provider = "google",
        ProviderSubject = subject, ProviderEmail = "google@gmail.test", EmailVerified = true,
        LinkedAt = _clock.UtcNow
    };

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.SetSystemMode();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseTestSqlServer(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddTestRls(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
