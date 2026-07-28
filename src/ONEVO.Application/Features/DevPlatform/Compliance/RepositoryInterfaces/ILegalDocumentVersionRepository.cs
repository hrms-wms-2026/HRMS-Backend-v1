using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

public interface ILegalDocumentVersionRepository
{
    Task<IReadOnlyList<LegalDocumentVersion>> GetCurrentRequiredVersionsAsync(CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetByDocumentTypeAndVersionAsync(string documentType, string version, CancellationToken ct = default);
    Task AddAsync(LegalDocumentVersion entity, CancellationToken ct = default);
    Task<IReadOnlyList<LegalDocumentVersion>> ListAsync(string? documentType, string? status, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetPublishedAsync(string documentType, string version, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetCurrentPublishedByDocumentTypeAsync(string documentType, CancellationToken ct = default);
}
