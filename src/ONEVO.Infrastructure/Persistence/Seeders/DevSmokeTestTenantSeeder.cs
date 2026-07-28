using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only seed data for local end-to-end smoke tests.
/// This does not create schema and must never be treated as production bootstrap.
/// </summary>
public sealed class DevSmokeTestTenantSeeder : IHostedService
{
    private static readonly Guid TenantId = Guid.Parse("da810816-3fed-4e71-9a44-f93e9b509bc7");
    private static readonly Guid UserId = Guid.Parse("c468afc2-967a-4b9a-beae-6bce6652ffc1");
    private static readonly Guid RoleId = Guid.Parse("70a8c52d-d8d8-4be2-b377-33e62088dfc4");
    private static readonly Guid SubscriptionId = Guid.Parse("be53e2b6-b1c5-4765-b4f3-c73ef5387908");

    private const string TenantSlug = "acme";
    private const string TenantName = "Acme Test";
    private const string UserEmail = "siyasiyamala932@gmail.com";
    private const string UserPassword = "Password123!";
    private const string RoleName = "Tenant Owner";
    private const string GitHubProvider = "github";
    private const string GitHubIntegrationKey = "github";
    private const string GitHubAuthorizationUrl = "https://github.com/login/oauth/authorize";
    private const string GitHubTokenUrl = "https://github.com/login/oauth/access_token";

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
                "Development smoke-test tenant seeded. Tenant user: {Email}",
                UserEmail);
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
        var tenant = await SeedTenantAsync(db, now, ct);
        await db.SaveChangesAsync(ct);

        ResolveSmokeTenantContext(tenantContext, tenant);
        var user = await SeedTenantUserAsync(db, tenant.Id, passwordHasher, now, ct);
        await SeedTenantAuthPolicyAsync(db, tenant.Id, now, ct);
        await SeedTenantOwnerRoleAsync(db, tenant.Id, user.Id, now, ct);
        await SeedTenantSubscriptionAsync(db, tenant.Id, user.Id, now, ct);
        await db.SaveChangesAsync(ct);

        tenantContext.SetAdminMode();
        await SeedGlobalEmailDirectoryAsync(db, tenant.Id, ct);
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

        ResolveSmokeTenantContext(tenantContext, tenant);
        await SeedGitHubTenantApprovalAsync(db, tenant.Id, user.Id, now, ct);
        await db.SaveChangesAsync(ct);
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
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = TenantId,
                Name = TenantName,
                Slug = TenantSlug,
                IndustryProfile = "office_it",
                CompanySizeRange = "51-200",
                Status = TenantStatus.Active,
                CreatedAt = now
            };
            db.Tenants.Add(tenant);
            return tenant;
        }

        tenant.Name = TenantName;
        tenant.Slug = TenantSlug;
        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAt = now;
        return tenant;
    }

    private static async Task<User> SeedTenantUserAsync(
        ApplicationDbContext db,
        Guid tenantId,
        IPasswordHasher passwordHasher,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Matched by the seeder's fixed UserId, not by email: an existing dev/test database seeded
        // before the smoke-tenant owner email changed still has this row under the old address, and
        // looking it up by email would fall through to the Add() branch below with the same
        // hardcoded Id - a primary-key violation. Id is the stable anchor; email is just a field on
        // the row it updates.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null)
        {
            user = new User
            {
                Id = UserId,
                TenantId = tenantId,
                Email = UserEmail,
                FirstName = "Acme",
                LastName = "Owner",
                PasswordHash = passwordHasher.Hash(UserPassword),
                IsActive = true,
                EmailVerified = true,
                MustChangePassword = false,
                PasswordSetByAdmin = false,
                CreatedAt = now,
                CreatedById = UserId
            };
            db.Users.Add(user);
            return user;
        }

        user.Email = UserEmail;
        user.FirstName = "Acme";
        user.LastName = "Owner";
        user.IsActive = true;
        user.EmailVerified = true;
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(UserPassword);
        }
        user.UpdatedAt = now;
        return user;
    }

    private static async Task SeedGlobalEmailDirectoryAsync(
        ApplicationDbContext db,
        Guid tenantId,
        CancellationToken ct)
    {
        // Remove any directory row left over from a previous seed's email for this tenant before
        // inserting the current one, so re-seeding an existing dev database never leaves a stale
        // entry for an address that no longer belongs to any user.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM global_email_directory
            WHERE tenant_id = {tenantId} AND email <> {UserEmail}
            """,
            ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO global_email_directory (email, tenant_id)
            VALUES ({UserEmail}, {tenantId})
            ON CONFLICT (email, tenant_id) DO NOTHING
            """,
            ct);
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

    private static async Task SeedTenantOwnerRoleAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Name == RoleName,
            ct);
        if (role is null)
        {
            role = new Role
            {
                Id = RoleId,
                TenantId = tenantId,
                Name = RoleName,
                Description = "Development smoke-test tenant owner.",
                IsSystem = true,
                CreatedAt = now,
                CreatedById = userId
            };
            db.Roles.Add(role);
        }
        else
        {
            role.Description = "Development smoke-test tenant owner.";
            role.IsSystem = true;
            role.UpdatedAt = now;
        }

        var permissions = await db.Permissions
            .Where(p => p.Code == "integrations:manage")
            .ToListAsync(ct);
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

    private static async Task SeedTenantSubscriptionAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid userId,
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
            .FirstOrDefaultAsync(s => s.Id == SubscriptionId, ct);
        if (subscription is null)
        {
            subscription = new TenantSubscription
            {
                Id = SubscriptionId,
                TenantId = tenantId,
                PlanId = plan.Id,
                BillingCycle = "monthly",
                Status = "trialing",
                CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime.Date),
                CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(30)),
                ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                CompanySizeRange = "51-200",
                SelectedModulesJson = """["integrations","work_management"]""",
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
        subscription.SelectedModulesJson = """["integrations","work_management"]""";
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
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }
}
