using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;

public interface ILegalAcceptanceRepository
{
    Task<IReadOnlyList<LegalAcceptanceRecord>> GetUserAcceptancesAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<LegalAcceptanceRecord?> GetUserAcceptanceForDocumentAsync(Guid tenantId, Guid userId, string documentType, string version, CancellationToken ct = default);
    Task AddAsync(LegalAcceptanceRecord record, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
