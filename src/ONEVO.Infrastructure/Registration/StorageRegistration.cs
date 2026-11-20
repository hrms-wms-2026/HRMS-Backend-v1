using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Entity assets and their access policies, storage quota and file storage.</summary>
internal static class StorageRegistration
{
    internal static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EfEntityAssetRepository>();
        services.AddScoped<IEntityAssetRepository>(sp => sp.GetRequiredService<EfEntityAssetRepository>());
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository>();
        services.AddScoped<ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository>(
            sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository>());
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeEntityAssetAccessPolicy>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.LegalEntityEntityAssetAccessPolicy>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Chats.ConversationEntityAssetAccessPolicy>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Documents.IEmployeeDocumentAccess,
            ONEVO.Application.Features.CoreHr.Employee.Documents.EmployeeDocumentAccess>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Access.IEmployeeViewAccess,
            ONEVO.Application.Features.CoreHr.Employee.Access.EmployeeViewAccess>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Access.IEmployeeInvitationAccess,
            ONEVO.Application.Features.CoreHr.Employee.Access.EmployeeInvitationAccess>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeDocumentAccessPolicy>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IPrimaryEntityAssetLinker,
            ONEVO.Application.Features.Storage.EntityAssets.Services.PrimaryEntityAssetLinker>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IEntityAssetAccessPolicyResolver>(sp =>
            new ONEVO.Application.Features.Storage.EntityAssets.Services.EntityAssetAccessPolicyResolver(
                new Dictionary<string, ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IEntityAssetAccessPolicy>
                {
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.Employee] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeEntityAssetAccessPolicy>(),
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.LegalEntity] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.LegalEntityEntityAssetAccessPolicy>(),
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.EmployeeDocument] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeDocumentAccessPolicy>(),
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.Conversation] =
                        sp.GetRequiredService<ONEVO.Application.Features.Inbox.Chats.ConversationEntityAssetAccessPolicy>()
                    // Part 2 plan (Project/Objective/Task endpoint consolidation) adds the remaining
                    // three owner types here when their controllers migrate to the generic resolve endpoint.
                }));

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
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IAvatarImageProcessor,
            ONEVO.Infrastructure.Services.Storage.File.AvatarImageProcessor>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IObjectStorageAdapter,
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ObjectStorageAdapter>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IFileStorageService,
            ONEVO.Infrastructure.Services.Storage.File.FileStorageService>();
        // SEC-13: no real scanner is deployed in any environment yet - see NullMalwareScanner's
        // doc comment. Not wired into the upload/read path until a scanner is selected (decision
        // gate in docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md).
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IMalwareScanner,
            ONEVO.Infrastructure.Services.Storage.File.NullMalwareScanner>();

        return services;
    }
}
