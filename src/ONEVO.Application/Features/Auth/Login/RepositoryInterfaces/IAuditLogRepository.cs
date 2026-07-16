using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken ct = default);
}
