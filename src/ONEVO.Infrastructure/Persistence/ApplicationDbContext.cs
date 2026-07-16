using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    private readonly AuditableEntityInterceptor _auditInterceptor;
    private readonly SoftDeleteInterceptor _softDeleteInterceptor;
    private readonly DomainEventDispatchInterceptor _domainEventInterceptor;
    private readonly ITenantContext _tenantContext;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntityInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        DomainEventDispatchInterceptor domainEventInterceptor,
        ITenantContext tenantContext)
        : base(options)
    {
        _auditInterceptor = auditInterceptor;
        _softDeleteInterceptor = softDeleteInterceptor;
        _domainEventInterceptor = domainEventInterceptor;
        _tenantContext = tenantContext;
    }

    // Infrastructure
    public DbSet<User> Users => Set<User>();

    // Auth
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<FeatureAccessGrant> FeatureAccessGrants => Set<FeatureAccessGrant>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<UserMfa> UserMfas => Set<UserMfa>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<GdprConsentRecord> GdprConsentRecords => Set<GdprConsentRecord>();
    public DbSet<UserExternalIdentity> UserExternalIdentities => Set<UserExternalIdentity>();
    public DbSet<TenantAuthPolicy> TenantAuthPolicies => Set<TenantAuthPolicy>();
    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();
    public DbSet<Position> Positions => Set<Position>();

    // Developer Platform (canonical Phase 1 inventory tables)
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformUserCredential> PlatformUserCredentials => Set<PlatformUserCredential>();
    public DbSet<PlatformUserInvite> PlatformUserInvites => Set<PlatformUserInvite>();
    public DbSet<PlatformRole> PlatformRoles => Set<PlatformRole>();
    public DbSet<PlatformPermission> PlatformPermissions => Set<PlatformPermission>();
    public DbSet<PlatformRolePermission> PlatformRolePermissions => Set<PlatformRolePermission>();
    public DbSet<PlatformUserRole> PlatformUserRoles => Set<PlatformUserRole>();
    public DbSet<PlatformUserSession> PlatformUserSessions => Set<PlatformUserSession>();
    public DbSet<PlatformAuthEvent> PlatformAuthEvents => Set<PlatformAuthEvent>();

    // Developer Platform
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantStatusHistory> TenantStatusHistories => Set<TenantStatusHistory>();

    // SharedPlatform
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<ModuleCatalogItem> ModuleCatalog => Set<ModuleCatalogItem>();
    public DbSet<ModuleFeature> ModuleFeatures => Set<ModuleFeature>();
    public DbSet<ModulePermissionOwnership> ModulePermissionOwnerships => Set<ModulePermissionOwnership>();
    public DbSet<ModuleCatalogPriceHistory> ModuleCatalogPriceHistories => Set<ModuleCatalogPriceHistory>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantProvisioningState> TenantProvisioningStates => Set<TenantProvisioningState>();
    public DbSet<TenantSetupSelection> TenantSetupSelections => Set<TenantSetupSelection>();
    public DbSet<TenantOneTimeCharge> TenantOneTimeCharges => Set<TenantOneTimeCharge>();

    // System Config - Payment Gateway (Phase 1 canonical tables)
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<PaymentGatewayCredential> PaymentGatewayCredentials => Set<PaymentGatewayCredential>();
    public DbSet<PaymentGatewayCountryRoute> PaymentGatewayCountryRoutes => Set<PaymentGatewayCountryRoute>();

    // System Config - Platform Service Keys (Phase 1 canonical table)
    public DbSet<PlatformServiceKey> PlatformServiceKeys => Set<PlatformServiceKey>();

    // System Config - Platform OAuth Apps (Phase 1 canonical tables)
    public DbSet<PlatformOAuthApp> PlatformOAuthApps => Set<PlatformOAuthApp>();
    public DbSet<PlatformOAuthAppCredential> PlatformOAuthAppCredentials => Set<PlatformOAuthAppCredential>();

    // System Config - Integration Catalog (Phase 1 canonical tables)
    public DbSet<IntegrationCatalogEntry> IntegrationCatalogEntries => Set<IntegrationCatalogEntry>();
    public DbSet<ModuleIntegrationLink> ModuleIntegrationLinks => Set<ModuleIntegrationLink>();
    public DbSet<TenantIntegrationCredential> TenantIntegrationCredentials => Set<TenantIntegrationCredential>();
    public DbSet<UserIntegrationConnection> UserIntegrationConnections => Set<UserIntegrationConnection>();

    // CoreHR
    public DbSet<Employee> Employees => Set<Employee>();

    // Lookups
    public DbSet<EmploymentType> EmploymentTypes => Set<EmploymentType>();
    public DbSet<EmploymentStatus> EmploymentStatuses => Set<EmploymentStatus>();
    public DbSet<WorkMode> WorkModes => Set<WorkMode>();
    public DbSet<ApprovalStatus> ApprovalStatuses => Set<ApprovalStatus>();
    public DbSet<Severity> Severities => Set<Severity>();

    // OrgStructure
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .AddInterceptors(_auditInterceptor, _softDeleteInterceptor, _domainEventInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwnedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var param = Expression.Parameter(entityType.ClrType, "e");

            // Tenant filter: only active when ContextMode == Tenant
            // System/Admin mode bypasses the tenant predicate (passes all rows through)
            var contextConst = Expression.Constant(_tenantContext);
            var isTenantMode = Expression.Equal(
                Expression.Property(contextConst, nameof(ITenantContext.ContextMode)),
                Expression.Constant(TenantContextMode.Tenant));
            var tenantIdProp = Expression.Property(param, nameof(ITenantOwnedEntity.TenantId));
            var tenantIdMatches = Expression.Equal(
                tenantIdProp,
                Expression.Property(contextConst, nameof(ITenantContext.TenantId)));
            // (ContextMode != Tenant) OR (TenantId == _tenantContext.TenantId)
            Expression filter = Expression.OrElse(Expression.Not(isTenantMode), tenantIdMatches);

            // BaseEntity subclasses have IsDeleted — always filter soft-deleted records
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var isDeletedProp = Expression.Property(param, nameof(BaseEntity.IsDeleted));
                filter = Expression.AndAlso(filter, Expression.Not(isDeletedProp));
            }

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(filter, param));
        }

        base.OnModelCreating(modelBuilder);
    }
}
