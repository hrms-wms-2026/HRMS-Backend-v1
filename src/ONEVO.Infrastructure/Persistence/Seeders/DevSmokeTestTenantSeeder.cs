using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only seed data for local end-to-end smoke tests.
/// This does not create schema and must never be treated as production bootstrap.
/// </summary>
public sealed class DevSmokeTestTenantSeeder : IHostedService
{
    private sealed record SmokeUserDefinition(
        Guid UserId,
        string Email,
        string FirstName,
        string LastName,
        Guid RoleId,
        string RoleName,
        string RoleDescription,
        IReadOnlyList<string>? PermissionCodes);

    private sealed record SmokeLegalEntityDefinition(
        Guid Id,
        string Name,
        string CompanyCode,
        string CountryCode,
        string CurrencyCode,
        string Timezone,
        bool IsPrimary);

    private sealed record SmokeEmployeeDefinition(
        Guid UserId,
        Guid LegalEntityId,
        string EmployeeNumber);

    private sealed record SmokeTenantDefinition(
        Guid TenantId,
        string Slug,
        string Name,
        Guid SubscriptionId,
        IReadOnlyList<SmokeUserDefinition> Users,
        IReadOnlyList<SmokeLegalEntityDefinition> LegalEntities,
        IReadOnlyList<SmokeEmployeeDefinition> Employees);

    private static readonly Guid AcmeTenantId = Guid.Parse("da810816-3fed-4e71-9a44-f93e9b509bc7");
    private static readonly Guid AcmeOwnerUserId = Guid.Parse("c468afc2-967a-4b9a-beae-6bce6652ffc1");
    private static readonly Guid AcmeOwnerRoleId = Guid.Parse("70a8c52d-d8d8-4be2-b377-33e62088dfc4");
    private static readonly Guid AcmeSubscriptionId = Guid.Parse("be53e2b6-b1c5-4765-b4f3-c73ef5387908");
    private static readonly Guid AcmeHrManagerUserId = Guid.Parse("1f02b6b1-3699-476e-bcb7-079172a3ede8");
    private static readonly Guid AcmeHrManagerRoleId = Guid.Parse("b59678fe-6295-4b89-8140-aed5b59e4f4c");
    private static readonly Guid AcmeWorkManagerUserId = Guid.Parse("bf868643-c87a-4a57-a84f-ab2005659650");
    private static readonly Guid AcmeWorkManagerRoleId = Guid.Parse("269883a4-4d4e-49e6-bc69-dcacb079168b");
    private static readonly Guid AcmeLegalEntityTechnologiesId = Guid.Parse("2addcd1b-e3d3-4930-b66f-e53329fa7f55");
    private static readonly Guid AcmeLegalEntitySolutionsId = Guid.Parse("04372560-2487-44ba-ac47-c41a6fc42ceb");
    private static readonly Guid AcmeLegalEntityGlobalServicesId = Guid.Parse("675710d1-2b10-4594-8c99-0c22183d2fd9");

    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private static readonly Guid DapiOwnerRoleId = Guid.Parse("722d06e9-23fd-403c-b34c-e0b12f81e974");
    private static readonly Guid DapiSubscriptionId = Guid.Parse("23a3a903-3690-4cad-8927-4e6c221b7465");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    private const string AcmeSlug = "acme";
    private const string AcmeTenantName = "Acme Test";
    private const string DapiSlug = "dapi";
    private const string DapiTenantName = "Dapi Test";
    private const string SmokeUserPassword = "Password123!";
    private const string FullAccessPermissionCode = "*";

    private const string AcmeOwnerEmail = "siyasiyamala932@gmail.com";
    private const string AcmeHrManagerEmail = "paramanathanmuthaiya@gmail.com";
    private const string AcmeWorkManagerEmail = "mrt15473@gmail.com";
    private const string DapiOwnerEmail = "dapiyshanth1908@gmail.com";

    private const string AcmeOwnerEmployeeNumber = "ACME-0001";
    private const string AcmeHrManagerEmployeeNumber = "ACME-0002";
    private const string AcmeWorkManagerEmployeeNumber = "ACME-0003";
    private const string DapiOwnerEmployeeNumber = "DAPI-0001";

    private const int SmokeDefaultEmploymentTypeId = 1;   // "full_time" - seeded by LookupDataSeeder
    private const int SmokeDefaultEmploymentStatusId = 1; // "active"    - seeded by LookupDataSeeder
    private const int SmokeDefaultWorkModeId = 1;          // "on_site"   - seeded by LookupDataSeeder

    private static readonly DateOnly SmokeEmployeeHireDate = new(2025, 1, 1);

    private const string GitHubProvider = "github";
    private const string GitHubIntegrationKey = "github";
    private const string GitHubAuthorizationUrl = "https://github.com/login/oauth/authorize";
    private const string GitHubTokenUrl = "https://github.com/login/oauth/access_token";

    private static readonly IReadOnlyList<string> HrManagerPermissionCodes =
    [
        "org:read", "org:manage", "employees:read", "employees:write", "roles:read"
    ];

    private static readonly IReadOnlyList<string> WorkManagerPermissionCodes =
    [
        "org:read", "employees:read", "projects:read", "tasks:read", "tasks:write"
    ];

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevSmokeTestTenantSeeder> _logger;

    public DevSmokeTestTenantSeeder(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<DevSmokeTestTenantSeeder> logger)
    {
        _services = services;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Test"))
        {
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

            tenantContext.SetAdminMode();
            await SeedAsync(
                db,
                tenantContext,
                passwordHasher,
                encryption,
                _configuration,
                cancellationToken);
            _logger.LogInformation(
                "Development smoke-test tenants seeded: {Slugs}",
                string.Join(", ", new[] { AcmeSlug, DapiSlug }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Development smoke-test tenant seeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IEncryptionService encryption,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        tenantContext.SetAdminMode();

        var tenantDefinitions = BuildTenantDefinitions();
        Tenant? acmeTenant = null;
        User? acmeOwnerUser = null;

        foreach (var tenantDefinition in tenantDefinitions)
        {
            tenantContext.SetAdminMode();
            var tenant = await SeedTenantAsync(db, tenantDefinition, now, ct);
            await db.SaveChangesAsync(ct);

            ResolveSmokeTenantContext(tenantContext, tenant);
            await SeedTenantLegalEntitiesAsync(db, tenant.Id, tenantDefinition.LegalEntities, now, ct);

            await EnsureSmokeEmployeeReferenceDataAsync(db, ct);

            User? firstUser = null;
            foreach (var userDefinition in tenantDefinition.Users)
            {
                var user = await SeedTenantUserAsync(db, tenant.Id, userDefinition, passwordHasher, now, ct);
                firstUser ??= user;
                await SeedTenantRoleAsync(db, tenant.Id, user.Id, userDefinition, now, ct);

                var employeeDefinition = tenantDefinition.Employees
                    .FirstOrDefault(e => e.UserId == user.Id);
                if (employeeDefinition is not null)
                {
                    await SeedTenantEmployeeAsync(db, tenant.Id, user, employeeDefinition, now, ct);
                }
            }

            await SeedTenantAuthPolicyAsync(db, tenant.Id, now, ct);
            await SeedTenantSubscriptionAsync(db, tenant.Id, firstUser!.Id, tenantDefinition.SubscriptionId, now, ct);
            await db.SaveChangesAsync(ct);

            tenantContext.SetAdminMode();
            var seededEmails = tenantDefinition.Users.Select(u => u.Email).ToArray();
            await SeedGlobalEmailDirectoryAsync(db, tenant.Id, seededEmails, ct);

            if (tenantDefinition.Slug == AcmeSlug)
            {
                acmeTenant = tenant;
                acmeOwnerUser = firstUser;
            }
        }

        await SeedDevelopmentLegalVersionsAsync(db, now, ct);

        var platformUser = await GetPlatformBootstrapUserAsync(db, ct);
        if (platformUser is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var oauthApp = await SeedGitHubPlatformOAuthAppAsync(
            db,
            configuration,
            platformUser.Id,
            now,
            ct);
        if (oauthApp is not null)
        {
            await SeedGitHubPlatformOAuthCredentialAsync(
                db,
                configuration,
                encryption,
                oauthApp.Id,
                platformUser.Id,
                now,
                ct);

            await SeedGitHubIntegrationCatalogAsync(db, platformUser.Id, now, ct);
            await SeedGitHubModuleIntegrationLinkAsync(db, platformUser.Id, now, ct);
        }

        await db.SaveChangesAsync(ct);

        if (oauthApp is null)
        {
            return;
        }

        // GitHub OAuth approval demo data is tenant-incidental and stays tied to Acme only -
        // it predates the dapi tenant and is out of scope for the multi-tenant seed expansion.
        ResolveSmokeTenantContext(tenantContext, acmeTenant!);
        await SeedGitHubTenantApprovalAsync(db, acmeTenant!.Id, acmeOwnerUser!.Id, now, ct);
        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<SmokeTenantDefinition> BuildTenantDefinitions()
    {
        return
        [
            new SmokeTenantDefinition(
                AcmeTenantId,
                AcmeSlug,
                AcmeTenantName,
                AcmeSubscriptionId,
                [
                    new SmokeUserDefinition(
                        AcmeOwnerUserId, AcmeOwnerEmail, "Acme", "Owner",
                        AcmeOwnerRoleId, "Tenant Owner",
                        "Development smoke-test tenant owner.", null),
                    new SmokeUserDefinition(
                        AcmeHrManagerUserId, AcmeHrManagerEmail, "Acme", "HR Manager",
                        AcmeHrManagerRoleId, "HR Manager",
                        "Development smoke-test HR/org manager role.", HrManagerPermissionCodes),
                    new SmokeUserDefinition(
                        AcmeWorkManagerUserId, AcmeWorkManagerEmail, "Acme", "Work Manager",
                        AcmeWorkManagerRoleId, "Work Manager",
                        "Development smoke-test work/limited manager role.", WorkManagerPermissionCodes)
                ],
                [
                    new SmokeLegalEntityDefinition(
                        AcmeLegalEntityTechnologiesId, "Acme Technologies", "ACME", "LK", "LKR", "Asia/Colombo", true),
                    new SmokeLegalEntityDefinition(
                        AcmeLegalEntitySolutionsId, "Acme Solutions", "ACMESOL", "LK", "LKR", "Asia/Colombo", false),
                    new SmokeLegalEntityDefinition(
                        AcmeLegalEntityGlobalServicesId, "Acme Global Services", "ACMEGS", "LK", "LKR", "Asia/Colombo", false)
                ],
                [
                    new SmokeEmployeeDefinition(AcmeOwnerUserId, AcmeLegalEntityTechnologiesId, AcmeOwnerEmployeeNumber),
                    new SmokeEmployeeDefinition(AcmeHrManagerUserId, AcmeLegalEntityTechnologiesId, AcmeHrManagerEmployeeNumber),
                    new SmokeEmployeeDefinition(AcmeWorkManagerUserId, AcmeLegalEntitySolutionsId, AcmeWorkManagerEmployeeNumber)
                ]),
            new SmokeTenantDefinition(
                DapiTenantId,
                DapiSlug,
                DapiTenantName,
                DapiSubscriptionId,
                [
                    new SmokeUserDefinition(
                        DapiOwnerUserId, DapiOwnerEmail, "Dapi", "Owner",
                        DapiOwnerRoleId, "Tenant Owner",
                        "Development smoke-test tenant owner.", null)
                ],
                [
                    new SmokeLegalEntityDefinition(
                        DapiLegalEntityId, "Dapi Technologies", "DAPI", "LK", "LKR", "Asia/Colombo", true)
                ],
                [
                    new SmokeEmployeeDefinition(DapiOwnerUserId, DapiLegalEntityId, DapiOwnerEmployeeNumber)
                ])
        ];
    }

    private static void ResolveSmokeTenantContext(
        IWritableTenantContext tenantContext,
        Tenant tenant)
    {
        tenantContext.Resolve(new TenantRegistryEntry(
            tenant.Id,
            tenant.Slug,
            tenant.Status,
            PlanCode: null));
    }

    private static async Task<Tenant> SeedTenantAsync(
        ApplicationDbContext db,
        SmokeTenantDefinition definition,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == definition.TenantId, ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = definition.TenantId,
                Name = definition.Name,
                Slug = definition.Slug,
                IndustryProfile = "office_it",
                CompanySizeRange = "51-200",
                Status = TenantStatus.Active,
                CreatedAt = now
            };
            db.Tenants.Add(tenant);
            return tenant;
        }

        tenant.Name = definition.Name;
        tenant.Slug = definition.Slug;
        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAt = now;
        return tenant;
    }

    private static async Task<User> SeedTenantUserAsync(
        ApplicationDbContext db,
        Guid tenantId,
        SmokeUserDefinition definition,
        IPasswordHasher passwordHasher,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Matched by the definition's fixed UserId, not by email: an existing dev/test database
        // seeded before an address changed still has the row under the old email, and looking it
        // up by email would fall through to the Add() branch below with the same hardcoded Id -
        // a primary-key violation. Id is the stable anchor; email is just a field on the row it
        // updates.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == definition.UserId, ct);
        if (user is null)
        {
            user = new User
            {
                Id = definition.UserId,
                TenantId = tenantId,
                Email = definition.Email,
                FirstName = definition.FirstName,
                LastName = definition.LastName,
                PasswordHash = passwordHasher.Hash(SmokeUserPassword),
                IsActive = true,
                EmailVerified = true,
                MustChangePassword = false,
                PasswordSetByAdmin = false,
                CreatedAt = now,
                CreatedById = definition.UserId
            };
            db.Users.Add(user);
            return user;
        }

        user.Email = definition.Email;
        user.FirstName = definition.FirstName;
        user.LastName = definition.LastName;
        user.IsActive = true;
        user.EmailVerified = true;
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(SmokeUserPassword);
        }
        user.UpdatedAt = now;
        return user;
    }

    private static async Task SeedGlobalEmailDirectoryAsync(
        ApplicationDbContext db,
        Guid tenantId,
        IReadOnlyList<string> emails,
        CancellationToken ct)
    {
        // Scoped cleanup: remove only rows for THIS tenant whose email is not one of the emails
        // this seeder currently seeds for THIS tenant (e.g. an address retired between seeder
        // versions). Never touches rows belonging to other tenants. The NOT IN list has a
        // variable number of emails, so the composite format string is built with numbered
        // placeholders and handed to FormattableStringFactory - values still flow through as
        // real DbParameters, never concatenated into the SQL text.
        var placeholders = string.Join(", ", emails.Select((_, index) => $"{{{index + 1}}}"));
        var parameters = new object[] { tenantId }.Concat(emails).ToArray();
        var deleteSql = FormattableStringFactory.Create(
            $"DELETE FROM global_email_directory WHERE tenant_id = {{0}} AND email NOT IN ({placeholders})",
            parameters);
        await db.Database.ExecuteSqlInterpolatedAsync(deleteSql, ct);

        foreach (var email in emails)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO global_email_directory (email, tenant_id)
                VALUES ({email}, {tenantId})
                ON CONFLICT (email, tenant_id) DO NOTHING
                """,
                ct);
        }
    }

    private static async Task SeedTenantAuthPolicyAsync(
        ApplicationDbContext db,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var policy = await db.TenantAuthPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (policy is null)
        {
            db.TenantAuthPolicies.Add(new TenantAuthPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PasswordCompletionAllowed = true,
                GoogleCompletionAllowed = true,
                GoogleEmailMismatchDefault = false,
                MfaRequired = false,
                CreatedAt = now
            });
            return;
        }

        policy.PasswordCompletionAllowed = true;
        policy.MfaRequired = false;
        policy.UpdatedAt = now;
    }

    private static async Task SeedTenantRoleAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId,
        SmokeUserDefinition userDefinition,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == userDefinition.RoleId, ct);
        if (role is null)
        {
            role = new Role
            {
                Id = userDefinition.RoleId,
                TenantId = tenantId,
                Name = userDefinition.RoleName,
                Description = userDefinition.RoleDescription,
                IsSystem = true,
                CreatedAt = now,
                CreatedById = userId
            };
            db.Roles.Add(role);
        }
        else
        {
            role.Name = userDefinition.RoleName;
            role.Description = userDefinition.RoleDescription;
            role.IsSystem = true;
            role.UpdatedAt = now;
        }

        var permissions = await ResolveRolePermissionsAsync(db, userDefinition.PermissionCodes, ct);
        foreach (var permission in permissions)
        {
            var exists = await db.RolePermissions.AnyAsync(
                rp => rp.TenantId == tenantId &&
                      rp.RoleId == role.Id &&
                      rp.PermissionId == permission.Id,
                ct);
            if (exists)
            {
                continue;
            }

            db.RolePermissions.Add(new RolePermission
            {
                TenantId = tenantId,
                RoleId = role.Id,
                PermissionId = permission.Id
            });
        }

        var assignmentExists = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == tenantId &&
                  ur.UserId == userId &&
                  ur.RoleId == role.Id,
            ct);
        if (!assignmentExists)
        {
            db.UserRoles.Add(new UserRole
            {
                TenantId = tenantId,
                UserId = userId,
                RoleId = role.Id,
                AssignedAt = now,
                AssignedBy = userId
            });
        }
    }

    private static async Task<List<Permission>> ResolveRolePermissionsAsync(
        ApplicationDbContext db,
        IReadOnlyList<string>? explicitCodes,
        CancellationToken ct)
    {
        if (explicitCodes is null)
        {
            // Tenant Owner: every currently seeded permission except the "*" bypass row - the
            // codebase already treats "*" as excluded from explicit tenant role grants
            // (DefaultRoleSeeder.SeedDefaultRolesAsync applies the same exclusion for its Owner
            // role), so that is the established "forbidden/internal-only" exclusion to reuse
            // here rather than inventing a new one.
            return await db.Permissions
                .Where(p => p.Code != FullAccessPermissionCode)
                .ToListAsync(ct);
        }

        var permissions = new List<Permission>(explicitCodes.Count);
        foreach (var code in explicitCodes)
        {
            var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
            if (permission is null)
            {
                throw new InvalidOperationException(
                    $"Development smoke-test seeder requires permission code '{code}' but it does " +
                    "not exist in the Permissions table. Add it to PermissionSeeder before seeding " +
                    "smoke-test roles.");
            }

            permissions.Add(permission);
        }

        return permissions;
    }

    private static async Task EnsureSmokeEmployeeReferenceDataAsync(
        ApplicationDbContext db,
        CancellationToken ct)
    {
        // LookupDataSeeder (DependencyInjection.cs) is registered and runs before
        // DevSmokeTestTenantSeeder in the hosted-service startup order, seeding fixed
        // Id=1 rows for employment_types("full_time"), employment_statuses("active"), and
        // work_modes("on_site"). This check turns a broken startup order into a clear failure
        // instead of silently writing Employee rows with dangling lookup ids.
        var typeOk = await db.EmploymentTypes.AnyAsync(t => t.Id == SmokeDefaultEmploymentTypeId, ct);
        var statusOk = await db.EmploymentStatuses.AnyAsync(s => s.Id == SmokeDefaultEmploymentStatusId, ct);
        var workModeOk = await db.WorkModes.AnyAsync(w => w.Id == SmokeDefaultWorkModeId, ct);

        if (!typeOk || !statusOk || !workModeOk)
        {
            throw new InvalidOperationException(
                "Development smoke-test seeder requires employment_types/employment_statuses/work_modes " +
                $"to already contain Id={SmokeDefaultEmploymentTypeId} rows (LookupDataSeeder must run " +
                "before DevSmokeTestTenantSeeder). Refusing to seed Employee rows with dangling lookup ids.");
        }
    }

    private static async Task SeedTenantEmployeeAsync(
        ApplicationDbContext db,
        Guid tenantId,
        User user,
        SmokeEmployeeDefinition definition,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var numberConflict = await db.Employees.FirstOrDefaultAsync(
            e => e.TenantId == tenantId &&
                 e.EmployeeNumber == definition.EmployeeNumber &&
                 e.UserId != user.Id,
            ct);
        if (numberConflict is not null)
        {
            throw new InvalidOperationException(
                $"Development smoke-test seeder found employee_number '{definition.EmployeeNumber}' " +
                $"in tenant {tenantId} already assigned to user {numberConflict.UserId}, but it is " +
                $"expected for user {user.Id}. The development database is in an inconsistent state " +
                "and must be reconciled manually before re-seeding.");
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id, ct);
        if (employee is null)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                EmployeeNumber = definition.EmployeeNumber,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                LegalEntityId = definition.LegalEntityId,
                EmploymentTypeId = SmokeDefaultEmploymentTypeId,
                EmploymentStatusId = SmokeDefaultEmploymentStatusId,
                WorkModeId = SmokeDefaultWorkModeId,
                HireDate = SmokeEmployeeHireDate,
                CreatedAt = now,
                CreatedById = user.Id
            });
            return;
        }

        employee.EmployeeNumber = definition.EmployeeNumber;
        employee.FirstName = user.FirstName;
        employee.LastName = user.LastName;
        employee.Email = user.Email;
        employee.LegalEntityId = definition.LegalEntityId;
        employee.EmploymentTypeId = SmokeDefaultEmploymentTypeId;
        employee.EmploymentStatusId = SmokeDefaultEmploymentStatusId;
        employee.WorkModeId = SmokeDefaultWorkModeId;
        employee.UpdatedAt = now;
    }

    private static async Task SeedTenantLegalEntitiesAsync(
        ApplicationDbContext db,
        Guid tenantId,
        IReadOnlyList<SmokeLegalEntityDefinition> definitions,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var definition in definitions)
        {
            var legalEntity = await db.LegalEntities.FirstOrDefaultAsync(l => l.Id == definition.Id, ct);
            if (legalEntity is null)
            {
                legalEntity = new LegalEntity
                {
                    Id = definition.Id,
                    TenantId = tenantId,
                    Name = definition.Name,
                    CompanyCode = definition.CompanyCode,
                    CountryCode = definition.CountryCode,
                    CurrencyCode = definition.CurrencyCode,
                    Timezone = definition.Timezone,
                    IsPrimary = definition.IsPrimary,
                    IsActive = true,
                    CreatedAt = now
                };
                db.LegalEntities.Add(legalEntity);
                continue;
            }

            legalEntity.Name = definition.Name;
            legalEntity.CompanyCode = definition.CompanyCode;
            legalEntity.CountryCode = definition.CountryCode;
            legalEntity.CurrencyCode = definition.CurrencyCode;
            legalEntity.Timezone = definition.Timezone;
            legalEntity.IsPrimary = definition.IsPrimary;
            legalEntity.IsActive = true;
            legalEntity.UpdatedAt = now;
        }
    }

    private static async Task SeedTenantSubscriptionAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId,
        Guid subscriptionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == "starter_51_200", ct);
        if (plan is null)
        {
            return;
        }

        var subscription = await db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (subscription is null)
        {
            subscription = new TenantSubscription
            {
                Id = subscriptionId,
                TenantId = tenantId,
                PlanId = plan.Id,
                BillingCycle = "monthly",
                Status = "trialing",
                CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime.Date),
                CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(30)),
                ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                CompanySizeRange = "51-200",
                SelectedModulesJson = plan.IncludedModulesJson ?? "[]",
                CalculatedMonthlyPrice = 0m,
                CalculatedAnnualPrice = 0m,
                BillingCurrency = "USD",
                CreatedById = userId,
                CreatedAt = now
            };
            db.TenantSubscriptions.Add(subscription);
            return;
        }

        subscription.Status = "trialing";
        // Always re-derive from the plan's current canonical module list rather than a
        // point-in-time literal, so a stale value already persisted in a dev database
        // (e.g. from before the plan's module list was corrected) self-heals on the next
        // backend startup instead of being permanently stuck.
        subscription.SelectedModulesJson = plan.IncludedModulesJson ?? "[]";
        subscription.UpdatedAt = now;
    }

    private static async Task<PlatformUser?> GetPlatformBootstrapUserAsync(
        ApplicationDbContext db,
        CancellationToken ct)
    {
        return await db.PlatformUsers
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(u => u.Status == PlatformUser.StatusActive, ct);
    }

    private static async Task<PlatformOAuthApp?> SeedGitHubPlatformOAuthAppAsync(
        ApplicationDbContext db,
        IConfiguration configuration,
        Guid platformUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var clientId = configuration["DevSmokeTest:GitHub:ClientId"];
        var app = await db.PlatformOAuthApps
            .FirstOrDefaultAsync(a => a.Provider == GitHubProvider, ct);

        if (app is null && string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        if (app is null)
        {
            app = new PlatformOAuthApp
            {
                Id = Guid.NewGuid(),
                Provider = GitHubProvider,
                AppName = "ONEVO GitHub Development",
                ClientId = clientId!.Trim(),
                AuthorizationUrl = GitHubAuthorizationUrl,
                TokenUrl = GitHubTokenUrl,
                DefaultScopes = ["read:user"],
                IsActive = true,
                UpdatedById = platformUserId,
                UpdatedAt = now
            };
            db.PlatformOAuthApps.Add(app);
            return app;
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            app.ClientId = clientId.Trim();
        }

        app.AppName = "ONEVO GitHub Development";
        app.AuthorizationUrl = GitHubAuthorizationUrl;
        app.TokenUrl = GitHubTokenUrl;
        app.DefaultScopes = ["read:user"];
        app.IsActive = true;
        app.UpdatedById = platformUserId;
        app.UpdatedAt = now;
        return app;
    }

    private static async Task SeedGitHubPlatformOAuthCredentialAsync(
        ApplicationDbContext db,
        IConfiguration configuration,
        IEncryptionService encryption,
        Guid appId,
        Guid platformUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var clientSecret = configuration["DevSmokeTest:GitHub:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        var activeCredentialExists = await db.PlatformOAuthAppCredentials.AnyAsync(
            c => c.PlatformOAuthAppId == appId && c.IsActive,
            ct);
        if (activeCredentialExists)
        {
            return;
        }

        db.PlatformOAuthAppCredentials.Add(new PlatformOAuthAppCredential
        {
            Id = Guid.NewGuid(),
            PlatformOAuthAppId = appId,
            ClientSecretEncrypted = encryption.Encrypt(clientSecret.Trim()),
            EncryptionKeyVersion = "v1",
            CredentialVersion = 1,
            IsActive = true,
            RotatedById = platformUserId,
            RotatedAt = now
        });
    }

    private static async Task SeedGitHubIntegrationCatalogAsync(
        ApplicationDbContext db,
        Guid platformUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entry = await db.IntegrationCatalogEntries
            .FirstOrDefaultAsync(e => e.IntegrationKey == GitHubIntegrationKey, ct);
        if (entry is null)
        {
            db.IntegrationCatalogEntries.Add(new IntegrationCatalogEntry
            {
                IntegrationKey = GitHubIntegrationKey,
                DisplayName = "GitHub",
                Description = "Connect GitHub accounts for work management activity.",
                ConnectionScope = "both",
                OnevoAppProvider = GitHubProvider,
                IsActive = true,
                CreatedById = platformUserId,
                CreatedAt = now
            });
            return;
        }

        entry.DisplayName = "GitHub";
        entry.Description = "Connect GitHub accounts for work management activity.";
        entry.ConnectionScope = "both";
        entry.OnevoAppProvider = GitHubProvider;
        entry.IsActive = true;
    }

    private static async Task SeedGitHubModuleIntegrationLinkAsync(
        ApplicationDbContext db,
        Guid platformUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var exists = await db.ModuleIntegrationLinks.AnyAsync(
            link => link.ModuleKey == "work_management" &&
                    link.IntegrationKey == GitHubIntegrationKey,
            ct);
        if (exists)
        {
            return;
        }

        db.ModuleIntegrationLinks.Add(new ModuleIntegrationLink
        {
            ModuleKey = "work_management",
            IntegrationKey = GitHubIntegrationKey,
            LinkedById = platformUserId,
            LinkedAt = now
        });
    }

    private static async Task SeedGitHubTenantApprovalAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var approval = await db.TenantIntegrationCredentials
            .FirstOrDefaultAsync(
                value => value.TenantId == tenantId &&
                         value.IntegrationKey == GitHubIntegrationKey,
                ct);
        if (approval is null)
        {
            db.TenantIntegrationCredentials.Add(new TenantIntegrationCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IntegrationKey = GitHubIntegrationKey,
                Status = "connected",
                ScopesGranted = [],
                ConnectedAt = now,
                ConnectedByUserId = userId
            });
            return;
        }

        approval.Status = "connected";
        approval.DisconnectedAt = null;
        approval.ErrorMessage = null;
        approval.ConnectedByUserId = userId;
        approval.ConnectedAt = approval.ConnectedAt == default ? now : approval.ConnectedAt;
    }

    private static async Task SeedDevelopmentLegalVersionsAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var termsExists = await db.LegalDocumentVersions.AnyAsync(
            v => v.DocumentType == "terms" && v.Version == "1.0", ct);
        if (!termsExists)
        {
            db.LegalDocumentVersions.Add(new ONEVO.Domain.Features.DevPlatform.Compliance.Entities.LegalDocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentType = "terms",
                Version = "1.0",
                Title = "ONEVO Terms & Conditions (Bootstrap Dev)",
                IsRequired = true,
                BlockScope = "dashboard",
                Status = "published",
                PublishedAt = now,
                PublishReason = "Development smoke-test baseline document.",
                ContentJson = LegalDocumentBootstrapContent.TermsJson,
                ContentHtml = LegalDocumentBootstrapContent.TermsHtml,
                ContentText = LegalDocumentBootstrapContent.TermsText,
                ContentHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.TermsHtml),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var privacyExists = await db.LegalDocumentVersions.AnyAsync(
            v => v.DocumentType == "privacy_notice" && v.Version == "1.0", ct);
        if (!privacyExists)
        {
            db.LegalDocumentVersions.Add(new ONEVO.Domain.Features.DevPlatform.Compliance.Entities.LegalDocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentType = "privacy_notice",
                Version = "1.0",
                Title = "ONEVO Privacy Notice (Bootstrap Dev)",
                IsRequired = true,
                BlockScope = "dashboard",
                Status = "published",
                PublishedAt = now,
                PublishReason = "Development smoke-test baseline document.",
                ContentJson = LegalDocumentBootstrapContent.PrivacyJson,
                ContentHtml = LegalDocumentBootstrapContent.PrivacyHtml,
                ContentText = LegalDocumentBootstrapContent.PrivacyText,
                ContentHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }
}
