using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Application.Features.Users.RepositoryInterfaces;

public interface IUserProfileRepository
{
    Task<Employee?> GetEmployeeByUserIdAsync(Guid userId, CancellationToken ct);
    Task<Employee?> GetEmployeeByIdAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyList<RegisteredAgent>> GetAgentsByEmployeeIdAsync(Guid employeeId, CancellationToken ct);
    Task<EmployeeWorkLocationSettings?> GetWorkLocationSettingsAsync(Guid employeeId, CancellationToken ct);
    Task<VerificationReferencePhoto?> GetActiveReferencePhotoAsync(Guid employeeId, CancellationToken ct);
}
