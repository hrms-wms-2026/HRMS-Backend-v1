using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

public interface ILegalDocumentVersionRepository
{
    Task<IReadOnlyList<LegalDocumentVersion>> GetCurrentRequiredVersionsAsync(CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetByDocumentTypeAndVersionAsync(string documentType, string version, CancellationToken ct = default);
    Task AddAsync(LegalDocumentVersion entity, CancellationToken ct = default);
}
