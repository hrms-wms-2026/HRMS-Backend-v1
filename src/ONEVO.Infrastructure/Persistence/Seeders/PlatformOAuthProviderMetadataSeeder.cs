using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only bootstrap seed for approved OAuth provider METADATA only
/// (github, google, microsoft, zoom - the fixed set in PlatformOAuthProviderCatalog).
/// This does not create schema, does not seed clientId/clientSecret/privateKey/
/// credential rows, and never activates a provider. It exists purely so the System
/// Config OAuth Apps screen has a provider card to configure in a fresh Development/Test
/// database, matching the "seeded metadata rows are supported provider cards, not fully
/// configured OAuth apps" rule.
///
/// Ordering: this seeder is registered to run AFTER DevSmokeTestTenantSeeder. That
/// seeder optionally creates a real, operator-configured "github" row when
/// DevSmokeTest:GitHub:ClientId is set. Running after it means: if that row exists with
/// a real clientId, this seeder leaves it completely untouched (see the "already has a
/// clientId" guard below); if it does not exist, this seeder creates a clean
/// metadata-only row instead of leaving the provider unrepresented.
/// </summary>
public sealed class PlatformOAuthProviderMetadataSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlatformOAuthProviderMetadataSeeder> _logger;

    public PlatformOAuthProviderMetadataSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<PlatformOAuthProviderMetadataSeeder> logger)
    {
        _services = services;
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

            await SeedAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform OAuth provider metadata seeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var bootstrapUser = await db.PlatformUsers
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(u => u.Status == PlatformUser.StatusActive, ct);
        if (bootstrapUser is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existingByProvider = await db.PlatformOAuthApps.ToDictionaryAsync(a => a.Provider, ct);

        foreach (var definition in PlatformOAuthProviderCatalog.GetAll())
        {
            if (existingByProvider.TryGetValue(definition.Provider, out var app))
            {
                // Never touch a row that already has real operator/dev configuration
                // (a non-empty clientId). Protocol metadata is backend-owned and always
                // safe to keep in sync since it was never operator-editable.
                if (!string.IsNullOrWhiteSpace(app.ClientId))
                {
                    continue;
                }

                app.AuthorizationUrl = definition.AuthorizationUrl;
                app.TokenUrl = definition.TokenUrl;
                app.DefaultScopes = definition.DefaultScopes;
                continue;
            }

            db.PlatformOAuthApps.Add(new PlatformOAuthApp
            {
                Id = Guid.NewGuid(),
                Provider = definition.Provider,
                AppName = definition.DisplayName,
                LogoUrl = null,
                ClientId = string.Empty,
                AuthorizationUrl = definition.AuthorizationUrl,
                TokenUrl = definition.TokenUrl,
                DefaultScopes = definition.DefaultScopes,
                IsActive = false,
                LastVerifiedAt = null,
                UpdatedById = bootstrapUser.Id,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
