using Amazon.Extensions.NETCore.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.ExternalIdentity;
using ONEVO.Infrastructure.Identity.Mfa;
using ONEVO.Infrastructure.Identity.Passwords;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.ExternalServices.GitHub;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Subscription;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Services.DevPlatform.Provisioning;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Services;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.TrayActivation;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;
using ONEVO.Infrastructure.Services.Auth.Login;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;
using ONEVO.Infrastructure.Services.Monitoring.CheckIn;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Infrastructure.Services.SystemConfig;

namespace ONEVO.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddScoped<TenantRlsInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(3);
                    npgsql.CommandTimeout(30);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<EfAuthRepository>();
        services.AddScoped<IRoleTemplateRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IUserRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IRefreshTokenRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<ISessionRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IUserMfaRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IRoleRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IRolePermissionRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IPermissionRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IUserPermissionOverrideRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IUserRoleRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IFeatureAccessGrantRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IAuditLogRepository>(sp => sp.GetRequiredService<EfAuthRepository>());

        // Developer Platform repositories
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantStatusHistoryRepository, EfTenantStatusHistoryRepository>();
        services.AddScoped<EfLegalEntityRepository>();
        services.AddScoped<ILegalEntityRepository>(sp => sp.GetRequiredService<EfLegalEntityRepository>());
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<EfSubscriptionRepository>();
        services.AddScoped<ISubscriptionPlanRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());
        services.AddScoped<ITenantSubscriptionRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());

        // Auth: invitation tokens
        services.AddScoped<IInvitationTokenRepository, EfInvitationTokenRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<IModuleCatalogRepository, ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ModuleCatalog.ModuleCatalogRepository>();

        // Auth: global email directory
        services.AddScoped<IGlobalEmailDirectoryRepository, EfGlobalEmailDirectoryRepository>();

        // Storage quota (Phase 1 tenant_storage_stats + enforcement service)
        services.AddScoped<
            ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces.ITenantStorageStatsRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota.EfTenantStorageStatsRepository>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IStorageQuotaService,
            ONEVO.Infrastructure.Services.Storage.Quota.StorageQuotaService>();

        // File Storage (Phase 1 file_records + file_upload_reservations)
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileRecordRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.File.EfFileRecordRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileUploadReservationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.File.EfFileUploadReservationRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IUploadPurposePolicy,
            ONEVO.Infrastructure.Services.Storage.File.UploadPurposePolicy>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IObjectStorageAdapter,
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ObjectStorageAdapter>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IFileStorageService,
            ONEVO.Infrastructure.Services.Storage.File.FileStorageService>();

        // System Config - Payment Gateway (Phase 1 canonical tables)
        services.AddScoped<IPaymentGatewayRepository, EfPaymentGatewayRepository>();
        services.AddScoped<IPaymentGatewayVerificationService, PaymentGatewayVerificationService>();
        services.AddScoped<
            IPaymentGatewayWebhookSecretResolver,
            PaymentGatewayWebhookSecretResolver>();

        // System Config - Platform Service Keys (Phase 1 canonical table)
        services.AddScoped<IPlatformServiceKeyRepository, EfPlatformServiceKeyRepository>();
        services.AddScoped<IPlatformServiceKeyVerificationService, PlatformServiceKeyVerificationService>();
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

        // Tenant-scoped connected integration credentials
        services.AddScoped<ITenantIntegrationCredentialRepository, EfTenantIntegrationCredentialRepository>();
        services.AddScoped<ITenantIntegrationCredentialResolver, TenantIntegrationCredentialResolver>();
        services.AddScoped<IUserIntegrationConnectionRepository, EfUserIntegrationConnectionRepository>();
        services.AddDataProtection();
        services.AddSingleton<IOAuthStateProtector, OAuthStateProtector>();
        services.AddHttpClient<IGitHubOAuthClient, GitHubOAuthTokenClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Tenant cache invalidation
        services.AddScoped<ITenantCacheInvalidator, TenantCacheInvalidator>();

        // Transactional outbox + idempotency
        services.AddScoped<IOutboxWriter, Services.SharedPlatform.Outbox.OutboxWriter>();
        services.AddScoped<IIdempotencyStore, Persistence.Repositories.SharedPlatform.Idempotency.EfIdempotencyStore>();
        services.AddHostedService<Services.SharedPlatform.Outbox.OutboxProcessor>();
        services.AddHostedService<Services.Auth.Login.LoginWorkspaceSelectionChallengeCleanupService>();

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

        services.AddHttpContextAccessor();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<ICurrentPlatformUserContext, CurrentPlatformUserContext>();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Monitoring - Tray App Activation
        services.AddScoped<ITrayActivationRepository, EfTrayActivationRepository>();
        services.AddSingleton<ITrayTokenService, TrayTokenService>();

        // Monitoring - Check-In
        services.AddScoped<ICheckInRepository, EfCheckInRepository>();
        services.AddScoped<ITrayCurrentDevice, TrayCurrentDeviceService>();

        // Monitoring - Work Sessions (clock-in/break/clock-out)
        services.AddScoped<IWorkSessionRepository, EfWorkSessionRepository>();

        // Monitoring - Activity (keyboard/mouse tracking)
        services.AddScoped<IActivitySnapshotRepository, EfActivitySnapshotRepository>();
        services.AddScoped<IActivityRawBufferRepository, EfActivityRawBufferRepository>();
        services.AddScoped<IAppUsageSnapshotRepository, EfAppUsageSnapshotRepository>();
        services.AddScoped<IDeviceStateSnapshotRepository, EfDeviceStateSnapshotRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces.IMeetingSignalRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings.EfMeetingSignalRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces.IBiometricEnrollmentAttemptRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics.EfBiometricEnrollmentAttemptRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces.IBiometricProfileRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics.EfBiometricProfileRepository>();
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<Amazon.Rekognition.IAmazonRekognition>();
        services.AddAWSService<Amazon.SecurityToken.IAmazonSecurityTokenService>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceLivenessService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceLivenessService>();
        services.AddScoped<IActivityDailySummaryRepository, EfActivityDailySummaryRepository>();
        services.AddScoped<IMonitoringToggleResolver, MonitoringToggleResolverService>();
        services.AddHostedService<ActivityDailySummaryJob>();

        // Monitoring - Screenshots
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces.IEvidenceAssetRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots.EfEvidenceAssetRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces.IAgentCommandRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots.EfAgentCommandRepository>();
        services.AddHostedService<ONEVO.Infrastructure.Services.Monitoring.Screenshots.AgentCommandExpiryJob>();

        // Auth services
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        // Runtime MFA challenge storage is always the PostgreSQL-backed mfa_challenges table.
        services.AddScoped<IMfaChallengeStore, PostgresMfaChallengeStore>();
        // Admin/platform MFA challenge storage — in-memory for now (v1), no PostgreSQL-backed
        // equivalent yet. Fine for a single-instance local/dev deployment; revisit before running
        // multiple API instances behind a load balancer.
        services.AddScoped<IPlatformMfaChallengeStore, MemoryPlatformMfaChallengeStore>();
        services.AddScoped<IBaseLoginCandidateRepository, EfBaseLoginCandidateRepository>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeRepository, EfLoginWorkspaceSelectionChallengeRepository>();
        services.AddScoped<IBaseLoginFixedWorkVerifier, BaseLoginFixedWorkVerifier>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeCleanupRunner, LoginWorkspaceSelectionChallengeCleanupRunner>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddSingleton<IPermissionVersionService, PermissionVersionService>();
        services.AddScoped<ITotpService, OtpNetTotpService>();
        services.AddScoped<ILoginSessionMaterialFactory, LoginSessionMaterialFactory>();
        services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.AddScoped<ILoginContinuationService, LoginContinuationService>();
        services.AddScoped<ITenantSessionExchangeChallengeRepository, EfTenantSessionExchangeChallengeRepository>();
        services.AddScoped<ITenantSessionExchangeService, TenantSessionExchangeService>();

        // Legal acceptance and versioning
        services.AddScoped<ILegalDocumentVersionRepository, EfLegalDocumentVersionRepository>();
        services.AddScoped<ILegalAcceptanceRepository, EfLegalAcceptanceRepository>();
        services.AddScoped<ILegalAcceptanceChecker, LegalAcceptanceChecker>();
        services.AddScoped<ILegalAcceptanceSubmissionService, LegalAcceptanceSubmissionService>();
        services.AddScoped<ILegalLoginChallengeRepository, EfLegalLoginChallengeRepository>();

        // Developer Platform access (canonical platform_* tables)
        services.AddScoped<EfPlatformAccessRepository>();
        services.AddScoped<IPlatformUserRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformUserCredentialRepository, EfPlatformUserCredentialRepository>();
        services.AddScoped<IPlatformRoleRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformUserSessionRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformAccessReadRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformAuthEventRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformUserInviteRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
        services.AddScoped<IPlatformPermissionResolver, PlatformPermissionResolver>();
        services.AddScoped<IPlatformAccessManagementService, PlatformAccessManagementService>();
        services.AddScoped<IUserExternalIdentityRepository, EfUserExternalIdentityRepository>();
        services.AddScoped<ITenantAuthPolicyRepository, EfTenantAuthPolicyRepository>();

        // -- Platform options binding ------------------------------------------
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<PlatformUrlsOptions>(configuration.GetSection(PlatformUrlsOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.StorageQuotaOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.StorageQuotaOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.FileStorageOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.FileStorageOptions.SectionName));
        services.AddOptions<AwsRekognitionOptions>()
            .Bind(configuration.GetSection(AwsRekognitionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ONEVO.Application.Common.Configuration.BiometricEnrollmentOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Common.Configuration.BiometricEnrollmentOptions.SectionName));

        // -- Email: transactional sending via ONEVO-owned platform service keys --
        // Both the active transactional email provider and its API key are resolved
        // per send by joining platform_providers to platform_service_keys through
        // IPlatformServiceKeyResolver - never from appsettings or a hardcoded default.
        services.AddHttpClient(SendGridEmailAdapter.HttpClientName);
        services.AddHttpClient(ResendEmailAdapter.HttpClientName);
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailProviderAdapter, SendGridEmailAdapter>();
        services.AddScoped<IEmailProviderAdapter, ResendEmailAdapter>();
        services.AddScoped<ITransactionalEmailSender, PlatformKeyTransactionalEmailSender>();
        services.AddScoped<IEmailService, TransactionalEmailService>();

        services.AddScoped<IEncryptionService, AesEncryptionService>();

        // Background seeder
        services.AddHostedService<PermissionSeeder>();
        services.AddHostedService<RoleTemplateSeeder>();
        services.AddHostedService<LookupDataSeeder>();
        services.AddHostedService<PlatformAccessSeeder>();
        services.AddHostedService<ModuleCatalogSeeder>();
        services.AddHostedService<DevSmokeTestTenantSeeder>();
        services.AddHostedService<PlatformOAuthProviderMetadataSeeder>();

        // Boot-time configuration audit (warns about missing keys; never fatal).
        services.AddHostedService<ConfigurationStartupValidator>();

        return services;
    }
}
