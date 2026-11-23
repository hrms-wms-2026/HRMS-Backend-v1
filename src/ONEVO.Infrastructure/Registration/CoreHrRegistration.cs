using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Employees, positions assignments, onboarding (single and bulk), checklists, offboarding and employment types.</summary>
internal static class CoreHrRegistration
{
    internal static IServiceCollection AddCoreHr(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPositionAssignmentRepository, EfPositionAssignmentRepository>();
        services.AddScoped<IEmployeeHierarchyClosureRepository, EfEmployeeHierarchyClosureRepository>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository,
            ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeProfileRepository,
            ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeProfileRepository>();
        services.AddScoped<IEmployeeVisibilityScopeResolver, EmployeeVisibilityScopeResolver>();
        services.AddScoped<ISeatEntitlementService, SeatEntitlementService>();
        services.AddScoped<IOnboardingDraftRepository, EfOnboardingDraftRepository>();
        services.AddScoped<IOnboardingDraftWriteService, OnboardingDraftWriteService>();
        services.AddScoped<IBulkOnboardingBatchRepository, EfBulkOnboardingBatchRepository>();
        services.AddScoped<IBulkOnboardingRowValidator, BulkOnboardingRowValidator>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch.IBulkOnboardingValidationRunner,
            ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch.BulkOnboardingValidationRunner>();
        services.AddScoped<IAccessGrantRequestRepository, EfAccessGrantRequestRepository>();
        services.AddScoped<ISeatIncreaseRequestRepository, EfSeatIncreaseRequestRepository>();
        services.AddScoped<IOnboardingInboxRepository, EfOnboardingInboxRepository>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.Services.OnboardingApprovers>();

        services.AddScoped<IChecklistTemplateRepository, EfChecklistTemplateRepository>();
        services.AddScoped<IEmployeeChecklistTaskRepository, EfEmployeeChecklistTaskRepository>();
        services.AddScoped<IOfficeProjectProvisioner, ONEVO.Infrastructure.Services.CoreHr.OfficeProjectProvisioner>();
        services.AddScoped<IOfficeProjectReconciler, ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciler>();

        services.AddScoped<IEmployeeChecklistWorkTaskProvisioner, EmployeeChecklistWorkTaskProvisioner>();
        services.AddScoped<IOffboardingRecordRepository, EfOffboardingRecordRepository>();
        services.AddScoped<IOffboardingTaskBypassRequestRepository, EfOffboardingTaskBypassRequestRepository>();
        services.AddScoped<IEmployeeOffboardingLockGuard, ONEVO.Infrastructure.Services.CoreHr.Offboarding.EmployeeOffboardingLockGuard>();
        services.AddScoped<IEmployeeOffboardingCoverageGuard, ONEVO.Infrastructure.Services.CoreHr.Offboarding.EmployeeOffboardingCoverageGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces.IEmployeeManageScopeGuard, ONEVO.Infrastructure.Services.CoreHr.EmployeeManageScopeGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces.IEmployeeReadAccessGuard, ONEVO.Infrastructure.Services.CoreHr.EmployeeReadAccessGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeActivityFeedRepository, ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeActivityFeedRepository>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces.IChecklistTemplateAssigneeResolver, ONEVO.Infrastructure.Services.CoreHr.Onboarding.ChecklistTemplateAssigneeResolver>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.Services.ChecklistTemplateTaskInputResolver>();

        services.AddScoped<IEmploymentTypeRepository, EfEmploymentTypeRepository>();

        services.AddScoped<
            ONEVO.Application.Features.CoreHr.OnboardingDraft.Services.IOnboardingWizardLookupService,
            ONEVO.Infrastructure.Services.CoreHr.OnboardingWizardLookupService>();

        return services;
    }
}
