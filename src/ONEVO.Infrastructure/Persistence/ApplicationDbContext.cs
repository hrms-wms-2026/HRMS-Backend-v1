using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
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

    /// <summary>
    /// Read by the tenant HasQueryFilter below via a reference to `this`, not
    /// a captured ITenantContext service instance. EF Core caches the
    /// compiled model once per process (keyed by DbContext CLR type), so a
    /// filter that closes over a specific scoped service object freezes that
    /// object's state for every later ApplicationDbContext instance in the
    /// process. A filter that instead references DbContext instance
    /// properties is resolved per DbContext instance at query time - EF Core
    /// treats `this`-typed constants in a query filter specially and
    /// substitutes the actual executing context, not the one that happened
    /// to build the model first.
    /// </summary>
    public bool IsTenantFilterActive => _tenantContext.ContextMode == TenantContextMode.Tenant;

    /// <summary>See remarks on <see cref="IsTenantFilterActive"/>.</summary>
    public Guid CurrentTenantId => _tenantContext.TenantId;

    // Monitoring - Tray App Activation
    public DbSet<TrayActivationCode> TrayActivationCodes => Set<TrayActivationCode>();
    public DbSet<TrayDeviceRegistration> TrayDeviceRegistrations => Set<TrayDeviceRegistration>();
    public DbSet<TrayDeviceRefreshToken> TrayDeviceRefreshTokens => Set<TrayDeviceRefreshToken>();

    // Monitoring - Employee Check-In
    public DbSet<EmployeeCheckIn> EmployeeCheckIns => Set<EmployeeCheckIn>();
    public DbSet<MonitoringFaceScan> MonitoringFaceScans => Set<MonitoringFaceScan>();

    // Monitoring - Work Sessions (clock-in/break/clock-out)
    public DbSet<EmployeeWorkSession> EmployeeWorkSessions => Set<EmployeeWorkSession>();

    // Monitoring - Activity (keyboard/mouse counts)
    public DbSet<ActivitySnapshot> ActivitySnapshots => Set<ActivitySnapshot>();
    public DbSet<ActivityRawBuffer> ActivityRawBuffers => Set<ActivityRawBuffer>();
    public DbSet<ActivityDailySummary> ActivityDailySummaries => Set<ActivityDailySummary>();
    public DbSet<ONEVO.Domain.Features.Monitoring.AppUsage.Entities.AppUsageSnapshot> AppUsageSnapshots => Set<ONEVO.Domain.Features.Monitoring.AppUsage.Entities.AppUsageSnapshot>();
    public DbSet<ONEVO.Domain.Features.Monitoring.DeviceState.Entities.DeviceStateSnapshot> DeviceStateSnapshots => Set<ONEVO.Domain.Features.Monitoring.DeviceState.Entities.DeviceStateSnapshot>();

    // Monitoring - Feature toggles & overrides
    public DbSet<MonitoringFeatureToggles> MonitoringFeatureToggles => Set<MonitoringFeatureToggles>();
    public DbSet<EmployeeMonitoringOverride> EmployeeMonitoringOverrides => Set<EmployeeMonitoringOverride>();
    public DbSet<MonitoringPolicyOverride> MonitoringPolicyOverrides => Set<MonitoringPolicyOverride>();

    // Monitoring - Screenshots & agent commands
    public DbSet<MonitoringEvidenceAsset> MonitoringEvidenceAssets => Set<MonitoringEvidenceAsset>();
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();

    // Infrastructure
    public DbSet<User> Users => Set<User>();

    // Storage quota (Phase 1 tenant_storage_stats)
    public DbSet<TenantStorageStats> TenantStorageStats => Set<TenantStorageStats>();

    // Storage files (Phase 1 file_records + file_upload_reservations)
    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<FileUploadReservation> FileUploadReservations => Set<FileUploadReservation>();

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
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();
    public DbSet<LoginWorkspaceSelectionChallenge> LoginWorkspaceSelectionChallenges => Set<LoginWorkspaceSelectionChallenge>();
    public DbSet<LegalLoginChallenge> LegalLoginChallenges => Set<LegalLoginChallenge>();
    public DbSet<TenantSessionExchangeChallenge> TenantSessionExchangeChallenges => Set<TenantSessionExchangeChallenge>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LegalDocumentVersion> LegalDocumentVersions => Set<LegalDocumentVersion>();
    public DbSet<LegalAcceptanceRecord> LegalAcceptanceRecords => Set<LegalAcceptanceRecord>();
    public DbSet<UserExternalIdentity> UserExternalIdentities => Set<UserExternalIdentity>();
    public DbSet<TenantAuthPolicy> TenantAuthPolicies => Set<TenantAuthPolicy>();
    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionReportingHistory> PositionReportingHistories => Set<PositionReportingHistory>();
    public DbSet<ManagementCoverageRecord> ManagementCoverageRecords => Set<ManagementCoverageRecord>();
    public DbSet<PositionAccessTemplate> PositionAccessTemplates => Set<PositionAccessTemplate>();

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
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<BillingAuditLog> BillingAuditLogs => Set<BillingAuditLog>();

    // System Config - Payment Gateway (Phase 1 canonical tables)
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<PaymentGatewayCredential> PaymentGatewayCredentials => Set<PaymentGatewayCredential>();
    public DbSet<PaymentGatewayCountryRoute> PaymentGatewayCountryRoutes => Set<PaymentGatewayCountryRoute>();

    // System Config - Platform Service Keys (Phase 1 canonical table)
    public DbSet<PlatformServiceKey> PlatformServiceKeys => Set<PlatformServiceKey>();

    // System Config - Provider Catalog (Phase 1 canonical table)
    public DbSet<PlatformProvider> PlatformProviders => Set<PlatformProvider>();

    // System Config - Platform OAuth Apps (Phase 1 canonical tables)
    public DbSet<PlatformOAuthApp> PlatformOAuthApps => Set<PlatformOAuthApp>();
    public DbSet<PlatformOAuthAppCredential> PlatformOAuthAppCredentials => Set<PlatformOAuthAppCredential>();

    // System Config - Integration Catalog (Phase 1 canonical tables)
    public DbSet<IntegrationCatalogEntry> IntegrationCatalogEntries => Set<IntegrationCatalogEntry>();
    public DbSet<ModuleIntegrationLink> ModuleIntegrationLinks => Set<ModuleIntegrationLink>();
    public DbSet<TenantIntegrationCredential> TenantIntegrationCredentials => Set<TenantIntegrationCredential>();
    public DbSet<UserIntegrationConnection> UserIntegrationConnections => Set<UserIntegrationConnection>();

    // System Config - Configuration Templates (Phase 1 canonical tables)
    public DbSet<ConfigurationTemplate> ConfigurationTemplates => Set<ConfigurationTemplate>();
    public DbSet<TenantConfigurationTemplateApplication> TenantConfigurationTemplateApplications => Set<TenantConfigurationTemplateApplication>();

    // CoreHR
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PositionAssignment> PositionAssignments => Set<PositionAssignment>();
    public DbSet<EmployeeHierarchyClosure> EmployeeHierarchyClosures => Set<EmployeeHierarchyClosure>();
    public DbSet<OnboardingDraft> OnboardingDrafts => Set<OnboardingDraft>();
    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();
    public DbSet<EmployeeChecklistTask> EmployeeChecklistTasks => Set<EmployeeChecklistTask>();
    public DbSet<AccessGrantRequest> AccessGrantRequests => Set<AccessGrantRequest>();

    // Lookups
    public DbSet<EmploymentType> EmploymentTypes => Set<EmploymentType>();
    public DbSet<EmploymentStatus> EmploymentStatuses => Set<EmploymentStatus>();
    public DbSet<WorkMode> WorkModes => Set<WorkMode>();
    public DbSet<ApprovalStatus> ApprovalStatuses => Set<ApprovalStatus>();
    public DbSet<Severity> Severities => Set<Severity>();

    // OrgStructure
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Department> Departments => Set<Department>();

    // Storage - EntityAssets (Phase 1 entity_assets, scoped to owner_type "project" for now)
    public DbSet<EntityAsset> EntityAssets => Set<EntityAsset>();

    // Work Management - Foundation slice
    public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<ObjectiveChangeRequest> ObjectiveChangeRequests => Set<ObjectiveChangeRequest>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectMemberInvitation> ProjectMemberInvitations => Set<ProjectMemberInvitation>();
    public DbSet<VersionStatus> VersionStatuses => Set<VersionStatus>();
    public DbSet<ProjectVersion> ProjectVersions => Set<ProjectVersion>();
    public DbSet<ReleaseCalendarEntry> ReleaseCalendarEntries => Set<ReleaseCalendarEntry>();
    public DbSet<Label> Labels => Set<Label>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .AddInterceptors(_auditInterceptor, _softDeleteInterceptor, _domainEventInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        var dbContextConst = Expression.Constant(this, typeof(ApplicationDbContext));
        var isTenantFilterActiveProperty = typeof(ApplicationDbContext).GetProperty(nameof(IsTenantFilterActive))!;
        var currentTenantIdProperty = typeof(ApplicationDbContext).GetProperty(nameof(CurrentTenantId))!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwnedEntity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType != null)
                continue;

            var composedFilter = ComposeTenantAndSoftDeleteFilter(
                entityType,
                dbContextConst,
                isTenantFilterActiveProperty,
                currentTenantIdProperty);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(composedFilter);
        }

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// HasQueryFilter replaces rather than combines the entity's existing
    /// (default-keyed) query filter. Some entity configurations - e.g.
    /// UserRoleConfiguration, RolePermissionConfiguration - declare their own
    /// HasQueryFilter for reasons unrelated to tenant scoping (hiding rows
    /// whose Role has been soft-deleted). Reading back whatever filter
    /// ApplyConfigurationsFromAssembly already recorded for the entity and
    /// AND-ing it with the generic tenant/soft-delete predicate here
    /// preserves that entity-specific condition instead of silently
    /// dropping it, while entities with no prior filter simply get the
    /// generic predicate on its own.
    /// </summary>
    private static LambdaExpression ComposeTenantAndSoftDeleteFilter(
        IMutableEntityType entityType,
        ConstantExpression dbContextConst,
        PropertyInfo isTenantFilterActiveProperty,
        PropertyInfo currentTenantIdProperty)
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");

        var genericFilterBody = BuildGenericTenantAndSoftDeleteFilterBody(
            entityType,
            parameter,
            dbContextConst,
            isTenantFilterActiveProperty,
            currentTenantIdProperty);

        // EF Core keys the query filter declared by a plain, unkeyed
        // HasQueryFilter(expression) call (the overload IEntityTypeConfiguration
        // implementations such as UserRoleConfiguration/RolePermissionConfiguration
        // use) under a null key, not string.Empty - FindDeclaredQueryFilter(null)
        // is what actually finds it.
        var existingFilter = entityType.FindDeclaredQueryFilter(null!);
        if (existingFilter is null)
        {
            return Expression.Lambda(genericFilterBody, parameter);
        }

        var existingLambda = existingFilter.Expression!;
        var existingParameter = existingLambda.Parameters[0];
        var existingBody = new QueryFilterParameterReplacer(existingParameter, parameter)
            .Visit(existingLambda.Body);

        var combinedBody = Expression.AndAlso(existingBody, genericFilterBody);
        return Expression.Lambda(combinedBody, parameter);
    }

    private static Expression BuildGenericTenantAndSoftDeleteFilterBody(
        IMutableEntityType entityType,
        ParameterExpression parameter,
        ConstantExpression dbContextConst,
        PropertyInfo isTenantFilterActiveProperty,
        PropertyInfo currentTenantIdProperty)
    {
        // Tenant filter: only active when IsTenantFilterActive is true.
        // System/Admin mode bypasses the tenant predicate (passes all rows through)
        var isTenantFilterActive = Expression.Property(dbContextConst, isTenantFilterActiveProperty);
        var tenantIdProp = Expression.Property(parameter, nameof(ITenantOwnedEntity.TenantId));
        var currentTenantId = Expression.Property(dbContextConst, currentTenantIdProperty);
        var tenantIdMatches = Expression.Equal(tenantIdProp, currentTenantId);
        // (!IsTenantFilterActive) OR (TenantId == CurrentTenantId)
        Expression filter = Expression.OrElse(Expression.Not(isTenantFilterActive), tenantIdMatches);

        // BaseEntity subclasses have IsDeleted - always filter soft-deleted records
        if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
        {
            var isDeletedProp = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            filter = Expression.AndAlso(filter, Expression.Not(isDeletedProp));
        }

        return filter;
    }

    private sealed class QueryFilterParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public QueryFilterParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _oldParameter ? _newParameter : base.VisitParameter(node);
    }
}
