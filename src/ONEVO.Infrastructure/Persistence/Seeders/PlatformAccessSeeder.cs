using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the canonical Developer Platform access data:
/// - platform_permissions from <see cref="PlatformPermissionCatalog"/> (deterministic, idempotent)
/// - the seven documented system roles from <see cref="PlatformRoleCatalog"/>
/// - all permissions granted to Platform Super Admin (other system roles are seeded
///   without grants — see PLATFORM_ROLE_PERMISSION_GAPS.md)
/// - an optional bootstrap Super Admin platform user from configuration
/// - in Development/Test only, an initial hashed password credential when none exists.
/// Configuration is bootstrap input only; runtime authentication and authorization resolve
/// from the database.
/// </summary>
public class PlatformAccessSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformAccessSeeder> _logger;
    private readonly IHostEnvironment _environment;

    public PlatformAccessSeeder(
        IServiceProvider services,
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<PlatformAccessSeeder> logger)
    {
        _services = services;
        _config = config;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var bootstrapEmail = _config["PlatformBootstrap:SuperAdminEmail"];
            var bootstrapFullName = _config["PlatformBootstrap:SuperAdminFullName"];
            var bootstrapPassword = _config["DevAdmin:Password"];
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var allowCredentialBootstrap =
                _environment.IsDevelopment() || _environment.IsEnvironment("Test");

            await SeedAsync(
                db,
                bootstrapEmail,
                bootstrapFullName,
                bootstrapPassword,
                allowCredentialBootstrap,
                passwordHasher,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform access seeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        string? bootstrapEmail,
        string? bootstrapFullName,
        string? bootstrapPassword,
        bool allowCredentialBootstrap,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        await SeedPermissionsAsync(db, ct);
        await SeedSystemRolesAsync(db, ct);
        await SeedSuperAdminGrantsAsync(db, ct);
        await BootstrapSuperAdminUserAsync(
            db,
            bootstrapEmail,
            bootstrapFullName,
            bootstrapPassword,
            allowCredentialBootstrap,
            passwordHasher,
            ct);
        await db.SaveChangesAsync(ct);
    }

    public static Task SeedAsync(
        ApplicationDbContext db,
        string? bootstrapEmail,
        string? bootstrapFullName,
        CancellationToken ct)
    {
        return SeedAsync(
            db,
            bootstrapEmail,
            bootstrapFullName,
            null,
            false,
            new ONEVO.Infrastructure.Identity.Passwords.BCryptPasswordHasher(),
            ct);
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var defined = PlatformPermissionCatalog.GetAll();
        var existingRows = await db.PlatformPermissions.ToListAsync(ct);

        var existingByCode = new Dictionary<string, PlatformPermission>(StringComparer.Ordinal);
        foreach (var row in existingRows)
        {
            existingByCode[row.Code] = row;
        }

        foreach (var definition in defined)
        {
            if (existingByCode.TryGetValue(definition.Code, out var row))
            {
                row.ModuleKey = definition.ModuleKey;
                row.Description = definition.Description;
                row.IsHighRisk = definition.IsHighRisk;
                continue;
            }

            db.PlatformPermissions.Add(new PlatformPermission
            {
                Code = definition.Code,
                ModuleKey = definition.ModuleKey,
                Description = definition.Description,
                IsHighRisk = definition.IsHighRisk
            });
        }
    }

    private static async Task SeedSystemRolesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var definedRoles = PlatformRoleCatalog.GetSystemRoles();
        var existingRoles = await db.PlatformRoles.ToListAsync(ct);

        var existingByName = new Dictionary<string, PlatformRole>(StringComparer.Ordinal);
        foreach (var role in existingRoles)
        {
            existingByName[role.Name] = role;
        }

        foreach (var definition in definedRoles)
        {
            if (existingByName.TryGetValue(definition.Name, out var role))
            {
                if (!role.IsSystem)
                    role.IsSystem = true;
                continue;
            }

            db.PlatformRoles.Add(new PlatformRole
            {
                Id = Guid.NewGuid(),
                Name = definition.Name,
                Description = definition.Description,
                IsSystem = true,
                IsActive = true,
                CreatedById = null
            });
        }
    }

    private static async Task SeedSuperAdminGrantsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var superAdmin = await FindRoleAsync(db, PlatformRoleCatalog.SuperAdmin, ct);
        if (superAdmin is null)
            return;

        var existingGrants = await db.PlatformRolePermissions
            .Where(rp => rp.RoleId == superAdmin.Id)
            .ToListAsync(ct);

        var grantedCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in existingGrants)
        {
            grantedCodes.Add(grant.PermissionCode);
        }

        foreach (var definition in PlatformPermissionCatalog.GetAll())
        {
            if (grantedCodes.Contains(definition.Code))
                continue;

            db.PlatformRolePermissions.Add(new PlatformRolePermission
            {
                RoleId = superAdmin.Id,
                PermissionCode = definition.Code,
                GrantedById = null
            });
        }
    }

    private static async Task BootstrapSuperAdminUserAsync(
        ApplicationDbContext db,
        string? bootstrapEmail,
        string? bootstrapFullName,
        string? bootstrapPassword,
        bool allowCredentialBootstrap,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bootstrapEmail))
            return;

        var email = bootstrapEmail.Trim().ToLowerInvariant();

        var user = await db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            // Also check pending local adds so repeated seeder calls stay idempotent.
            foreach (var entry in db.ChangeTracker.Entries<PlatformUser>())
            {
                if (entry.Entity.Email == email)
                {
                    user = entry.Entity;
                    break;
                }
            }
        }

        if (user is null)
        {
            user = new PlatformUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = string.IsNullOrWhiteSpace(bootstrapFullName) ? email : bootstrapFullName.Trim(),
                Status = PlatformUser.StatusActive,
                MfaStatus = PlatformUser.MfaNotEnrolled,
                InviteStatus = PlatformUser.InviteAccepted,
                CreatedById = null
            };
            db.PlatformUsers.Add(user);
        }

        var superAdmin = await FindRoleAsync(db, PlatformRoleCatalog.SuperAdmin, ct);
        if (superAdmin is null)
            return;

        var hasAssignment = await db.PlatformUserRoles
            .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == superAdmin.Id, ct);
        if (!hasAssignment)
        {
            foreach (var entry in db.ChangeTracker.Entries<PlatformUserRole>())
            {
                if (entry.Entity.UserId == user.Id && entry.Entity.RoleId == superAdmin.Id)
                {
                    hasAssignment = true;
                    break;
                }
            }
        }

        if (!hasAssignment)
        {
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                UserId = user.Id,
                RoleId = superAdmin.Id,
                AssignedById = null
            });
        }

        await BootstrapPasswordCredentialAsync(
            db,
            user,
            bootstrapPassword,
            allowCredentialBootstrap,
            passwordHasher,
            ct);
    }

    private static async Task BootstrapPasswordCredentialAsync(
        ApplicationDbContext db,
        PlatformUser user,
        string? bootstrapPassword,
        bool allowCredentialBootstrap,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (!allowCredentialBootstrap)
        {
            return;
        }

        var exists = await db.PlatformUserCredentials.AnyAsync(
            credential =>
                credential.PlatformUserId == user.Id &&
                credential.CredentialType == PlatformUserCredential.PasswordType &&
                credential.RevokedAt == null,
            ct);
        if (exists)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<PlatformUserCredential>())
        {
            var credential = entry.Entity;
            if (credential.PlatformUserId == user.Id &&
                credential.CredentialType == PlatformUserCredential.PasswordType &&
                credential.RevokedAt is null)
            {
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            throw new InvalidOperationException(
                "Developer Platform bootstrap user has no active password credential. " +
                "Set DevAdmin__Password once in the local Development/Test environment, " +
                "then remove it after platform_user_credentials contains the password hash.");
        }

        var now = DateTimeOffset.UtcNow;
        db.PlatformUserCredentials.Add(new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType,
            PasswordHash = passwordHasher.Hash(bootstrapPassword),
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm,
            PasswordChangedAt = now,
            MustChangePassword = false,
            FailedLoginCount = 0,
            CreatedAt = now
        });
    }

    private static async Task<PlatformRole?> FindRoleAsync(ApplicationDbContext db, string name, CancellationToken ct)
    {
        var role = await db.PlatformRoles.FirstOrDefaultAsync(r => r.Name == name, ct);
        if (role is not null)
            return role;

        foreach (var entry in db.ChangeTracker.Entries<PlatformRole>())
        {
            if (entry.Entity.Name == name)
                return entry.Entity;
        }

        return null;
    }
}
