using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IDefaultRoleSeeder
{
    Task<IReadOnlyList<Role>> SeedDefaultRolesAsync(
        Guid tenantId,
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default);

    Task<Role> SeedOwnerRoleAsync(
        Guid tenantId,
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default);
}
