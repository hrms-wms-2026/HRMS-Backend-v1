using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Subscription;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Services.DevPlatform.Provisioning;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Infrastructure.Services.SystemConfig;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Tenants, subscriptions and billing, the module catalog, system configuration, provisioning, support and platform access.</summary>
internal static class DeveloperPlatformRegistration
{
    internal static IServiceCollection AddDeveloperPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        // Developer Platform repositories
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantStatusHistoryRepository, EfTenantStatusHistoryRepository>();
        services.AddScoped<EfLegalEntityRepository>();
        services.AddScoped<ILegalEntityRepository>(sp => sp.GetRequiredService<EfLegalEntityRepository>());

        services.AddScoped<EfSubscriptionRepository>();
        services.AddScoped<ISubscriptionPlanRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());
        services.AddScoped<ITenantSubscriptionRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());
        services.AddScoped<EfSubscriptionInvoiceRepository>();
        services.AddScoped<ISubscriptionInvoiceRepository>(sp => sp.GetRequiredService<EfSubscriptionInvoiceRepository>());
        services.AddScoped<EfBillingAuditLogRepository>();
        services.AddScoped<IBillingAuditLogRepository>(sp => sp.GetRequiredService<EfBillingAuditLogRepository>());

        services.AddScoped<IModuleCatalogRepository, ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ModuleCatalog.ModuleCatalogRepository>();

        // System Config - Payment Gateway (Phase 1 canonical tables)
        services.AddScoped<IPaymentGatewayRepository, EfPaymentGatewayRepository>();
        services.AddScoped<IPaymentGatewayVerificationService, PaymentGatewayVerificationService>();
        services.AddScoped<
            IPaymentGatewayWebhookSecretResolver,
            PaymentGatewayWebhookSecretResolver>();

        // System Config - Platform Service Keys (Phase 1 canonical table)
        services.AddScoped<IPlatformServiceKeyRepository, EfPlatformServiceKeyRepository>();
        services.AddScoped<ITrayAppReleaseRepository, EfTrayAppReleaseRepository>();
        // Task 47: rate limit overrides for the admin API.
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces.IRateLimitSettingRepository,
            EfRateLimitSettingRepository>();
        services.AddScoped<IPlatformServiceKeyVerificationService, PlatformServiceKeyVerificationService>();
        services.AddScoped<
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.IAwsRekognitionConnectionProbe,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.AwsRekognitionConnectionProbe>();
        services.AddScoped<
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.ICloudflareR2ConnectionProbe,
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ConnectionProbe>();
        services.AddScoped<IPlatformServiceKeyResolver, PlatformServiceKeyResolver>();

        // System Config - metadata-only provider catalog
        services.AddScoped<IPlatformProviderRepository, EfPlatformProviderRepository>();

        // System Config - Platform OAuth Apps (Phase 1 canonical tables)
        services.AddScoped<IPlatformOAuthAppRepository, EfPlatformOAuthAppRepository>();
        services.AddScoped<IPlatformOAuthAppResolver, PlatformOAuthAppResolver>();

        // System Config - Integration Catalog (Phase 1 canonical tables)
        services.AddScoped<IIntegrationCatalogRepository, EfIntegrationCatalogRepository>();

        // System Config - Configuration Templates (Phase 1 canonical tables)
        services.AddScoped<IConfigurationTemplateRepository, EfConfigurationTemplateRepository>();
        services.AddScoped<ITenantConfigurationTemplateApplicationRepository, EfTenantConfigurationTemplateApplicationRepository>();

        // Tenant cache invalidation
        services.AddScoped<ITenantCacheInvalidator, TenantCacheInvalidator>();

        // Provisioning services
        services.AddScoped<ITenantOwnerInvitationService, TenantOwnerInvitationService>();
        services.AddScoped<IModuleEntitlementService, ModuleEntitlementService>();
        services.AddScoped<IModuleCatalogService, ModuleCatalogService>();
        services.AddScoped<ITenantPermissionCatalogService, TenantPermissionCatalogService>();
        services.AddScoped<IDefaultRoleSeeder, DefaultRoleSeeder>();

        // Provisioning section readers, backed by the data CreateTenantCommandHandler
        // seeds at tenant creation (tenant_subscriptions, its selected_modules_json,
        // tenant.settings_json, and the seeded system roles).
        services.AddScoped<ITenantSubscriptionStatusReader, TenantSubscriptionStatusReader>();
        services.AddScoped<ITenantModuleStatusReader, TenantModuleStatusReader>();
        services.AddScoped<ITenantRoleStatusReader, TenantRoleStatusReader>();
        services.AddScoped<ITenantSettingsStatusReader, TenantSettingsStatusReader>();

        // Platform support: tickets, attachments, announcements
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces.ISupportTicketRepository,
            ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support.EfSupportTicketRepository>();
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces.IPlatformAnnouncementRepository,
            ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support.EfPlatformAnnouncementRepository>();
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.Support.Services.ISupportAttachmentService,
            ONEVO.Application.Features.DevPlatform.Support.Services.SupportAttachmentService>();
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.Support.Services.ISupportNotifier,
            ONEVO.Infrastructure.Services.DevPlatform.Support.SupportNotifier>();

        // Developer Platform access (canonical platform_* tables)
        services.AddScoped<IPlatformUserRepository, EfPlatformUserRepository>();
        services.AddScoped<IPlatformUserCredentialRepository, EfPlatformUserCredentialRepository>();
        services.AddScoped<IPlatformRoleRepository, EfPlatformRoleRepository>();
        services.AddScoped<IPlatformUserSessionRepository, EfPlatformUserSessionRepository>();
        services.AddScoped<IPlatformAccessReadRepository, EfPlatformAccessReadRepository>();
        services.AddScoped<IPlatformAuthEventRepository, EfPlatformAuthEventRepository>();
        services.AddScoped<IPlatformUserInviteRepository, EfPlatformUserInviteRepository>();
        services.AddScoped<IPlatformPermissionResolver, PlatformPermissionResolver>();
        services.AddScoped<IPlatformAccessManagementService, PlatformAccessManagementService>();
        services.AddScoped<IUserExternalIdentityRepository, EfUserExternalIdentityRepository>();
        services.AddScoped<ITenantAuthPolicyRepository, EfTenantAuthPolicyRepository>();

        return services;
    }
}
