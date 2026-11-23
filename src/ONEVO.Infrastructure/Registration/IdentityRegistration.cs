using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Services;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Identity.ExternalIdentity;
using ONEVO.Infrastructure.Identity.Mfa;
using ONEVO.Infrastructure.Identity.Passwords;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.Auth.Login;
using ONEVO.Infrastructure.SharedState;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Users, roles, permissions, sessions, invitations, sign-in, MFA, password hashing and legal acceptance.</summary>
internal static class IdentityRegistration
{
    internal static IServiceCollection AddIdentityAndAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRoleTemplateRepository, EfRoleTemplateRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
        services.AddScoped<ISessionRepository, EfSessionRepository>();
        services.AddScoped<ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces.IUserAccountRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Auth.UserAccounts.EfUserAccountRepository>();
        services.AddScoped<IPasswordResetTokenRepository, EfPasswordResetTokenRepository>();
        services.AddScoped<IUserMfaRepository, EfUserMfaRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, EfUserPermissionOverrideRepository>();
        services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
        services.AddScoped<IFeatureAccessGrantRepository, EfFeatureAccessGrantRepository>();
        services.AddScoped<IAuditLogRepository, EfAuditLogRepository>();

        // Auth: invitation tokens
        services.AddScoped<IInvitationTokenRepository, EfInvitationTokenRepository>();

        // Auth: global email directory
        services.AddScoped<IGlobalEmailDirectoryRepository, EfGlobalEmailDirectoryRepository>();

        // Auth services
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        // Runtime MFA challenge storage is always the PostgreSQL-backed mfa_challenges table.
        services.AddScoped<IMfaChallengeStore, PostgresMfaChallengeStore>();
        // Admin/platform MFA challenge storage — in-memory for now (v1), no PostgreSQL-backed
        // equivalent yet. Fine for a single-instance local/dev deployment; revisit before running
        // multiple API instances behind a load balancer.
        // Task 21: shared in Redis when configured, so the password and code steps may hit different instances.
        if (SharedState.SharedStateRegistration.IsRedisEnabled(configuration))
            services.AddScoped<IPlatformMfaChallengeStore, SharedState.RedisPlatformMfaChallengeStore>();
        else
            services.AddScoped<IPlatformMfaChallengeStore, MemoryPlatformMfaChallengeStore>();
        services.AddScoped<IBaseLoginCandidateRepository, EfBaseLoginCandidateRepository>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeRepository, EfLoginWorkspaceSelectionChallengeRepository>();
        services.AddScoped<IBaseLoginFixedWorkVerifier, BaseLoginFixedWorkVerifier>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeCleanupRunner, LoginWorkspaceSelectionChallengeCleanupRunner>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();

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

        return services;
    }
}
