using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;

public interface IVerificationRepository
{
    Task<VerificationPolicy?> GetActivePolicyAsync(CancellationToken ct);
    Task<VerificationReferencePhoto?> GetActiveReferencePhotoAsync(
        Guid employeeId, CancellationToken ct);
    Task<VerificationReferencePhoto?> GetReferencePhotoAsync(Guid id, CancellationToken ct);
    Task<VerificationRecord?> GetVerificationRecordAsync(Guid id, CancellationToken ct);
    Task<EmployeeRemoteWorkProfile?> GetActiveRemoteProfileAsync(
        Guid employeeId, CancellationToken ct);
    Task<GdprConsentRecord?> GetLatestConsentAsync(
        Guid tenantId,
        Guid userId,
        string consentType,
        CancellationToken ct);
    Task<EmployeeRemoteWorkProfile?> GetRemoteProfileAsync(Guid id, CancellationToken ct);
    Task<RemoteWorkLocationChangeRequest?> GetPendingRemoteChangeAsync(
        Guid employeeId, CancellationToken ct);
    Task<IReadOnlyList<RemoteWorkLocationChangeRequest>>
        GetPendingRemoteChangesAsync(int skip, int take, CancellationToken ct);
    Task<RemoteWorkLocationChangeRequest?> GetRemoteChangeRequestAsync(
        Guid id, CancellationToken ct);
    Task AddVerificationRecordAsync(VerificationRecord record, CancellationToken ct);
    Task AddEvidenceAssetAsync(VerificationEvidenceAsset asset, CancellationToken ct);
    Task AddReferencePhotoAsync(VerificationReferencePhoto photo, CancellationToken ct);
    Task AddConsentAsync(GdprConsentRecord consent, CancellationToken ct);
    Task AddRemoteProfileAsync(EmployeeRemoteWorkProfile profile, CancellationToken ct);
    Task AddRemoteChangeRequestAsync(
        RemoteWorkLocationChangeRequest request, CancellationToken ct);
}
