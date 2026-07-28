using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.IdentityVerification;

public sealed class EfVerificationRepository : IVerificationRepository
{
    private readonly ApplicationDbContext _db;

    public EfVerificationRepository(ApplicationDbContext db) => _db = db;

    public Task<VerificationPolicy?> GetActivePolicyAsync(CancellationToken ct) =>
        _db.VerificationPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.IsActive, ct);

    public Task<VerificationReferencePhoto?> GetActiveReferencePhotoAsync(
        Guid employeeId, CancellationToken ct) =>
        _db.VerificationReferencePhotos
            .AsNoTracking()
            .SingleOrDefaultAsync(
                photo => photo.EmployeeId == employeeId &&
                         photo.IsActive &&
                         photo.Status == "approved",
                ct);

    public Task<VerificationReferencePhoto?> GetReferencePhotoAsync(
        Guid id, CancellationToken ct) =>
        _db.VerificationReferencePhotos.SingleOrDefaultAsync(photo => photo.Id == id, ct);

    public Task<VerificationRecord?> GetVerificationRecordAsync(
        Guid id, CancellationToken ct) =>
        _db.VerificationRecords.SingleOrDefaultAsync(record => record.Id == id, ct);

    public Task<EmployeeRemoteWorkProfile?> GetActiveRemoteProfileAsync(
        Guid employeeId, CancellationToken ct) =>
        _db.EmployeeRemoteWorkProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                profile => profile.EmployeeId == employeeId && profile.Status == "active",
                ct);

    public Task<GdprConsentRecord?> GetLatestConsentAsync(
        Guid tenantId,
        Guid userId,
        string consentType,
        CancellationToken ct) =>
        _db.GdprConsentRecords
            .AsNoTracking()
            .Where(consent =>
                consent.TenantId == tenantId &&
                consent.UserId == userId &&
                consent.ConsentType == consentType)
            .OrderByDescending(consent => consent.ConsentedAt)
            .ThenByDescending(consent => consent.Id)
            .FirstOrDefaultAsync(ct);

    public Task<EmployeeRemoteWorkProfile?> GetRemoteProfileAsync(
        Guid id, CancellationToken ct) =>
        _db.EmployeeRemoteWorkProfiles.SingleOrDefaultAsync(profile => profile.Id == id, ct);

    public Task<RemoteWorkLocationChangeRequest?> GetPendingRemoteChangeAsync(
        Guid employeeId, CancellationToken ct) =>
        _db.RemoteWorkLocationChangeRequests.SingleOrDefaultAsync(
            request => request.EmployeeId == employeeId && request.Status == "pending",
            ct);

    public async Task<IReadOnlyList<RemoteWorkLocationChangeRequest>>
        GetPendingRemoteChangesAsync(
            int skip,
            int take,
            CancellationToken ct) =>
        await _db.RemoteWorkLocationChangeRequests
            .AsNoTracking()
            .Where(request => request.Status == "pending")
            .OrderBy(request => request.RequestedAt)
            .ThenBy(request => request.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<RemoteWorkLocationChangeRequest?> GetRemoteChangeRequestAsync(
        Guid id, CancellationToken ct) =>
        _db.RemoteWorkLocationChangeRequests.SingleOrDefaultAsync(
            request => request.Id == id,
            ct);

    public async Task AddVerificationRecordAsync(
        VerificationRecord record, CancellationToken ct) =>
        await _db.VerificationRecords.AddAsync(record, ct);

    public async Task AddEvidenceAssetAsync(
        VerificationEvidenceAsset asset, CancellationToken ct) =>
        await _db.VerificationEvidenceAssets.AddAsync(asset, ct);

    public async Task AddReferencePhotoAsync(
        VerificationReferencePhoto photo, CancellationToken ct) =>
        await _db.VerificationReferencePhotos.AddAsync(photo, ct);

    public async Task AddConsentAsync(GdprConsentRecord consent, CancellationToken ct) =>
        await _db.GdprConsentRecords.AddAsync(consent, ct);

    public async Task AddRemoteProfileAsync(
        EmployeeRemoteWorkProfile profile, CancellationToken ct) =>
        await _db.EmployeeRemoteWorkProfiles.AddAsync(profile, ct);

    public async Task AddRemoteChangeRequestAsync(
        RemoteWorkLocationChangeRequest request, CancellationToken ct) =>
        await _db.RemoteWorkLocationChangeRequests.AddAsync(request, ct);
}
