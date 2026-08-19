using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;

/// <summary>Rejects mutation of an employee whose EmploymentStatusId is Resigned/Terminated.
/// Only ChangeEmployeePositionCommandHandler calls this today - every self-service me/* write is
/// already transitively blocked because User.IsActive=false (set at offboarding completion) fails
/// authentication on the very next request via TenantDatabaseTicketStore.RetrieveAsync, so no
/// other guard call site exists as of this codebase's current write surface. See design spec §7.</summary>
public interface IEmployeeOffboardingLockGuard
{
    Task<Result?> EnsureMutable(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
