namespace ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

public interface IPermissionVersionService
{
    Task<long> GetCurrentVersionAsync(Guid userId, CancellationToken ct = default);
    Task<long> IncrementVersionAsync(Guid userId, CancellationToken ct = default);
}
