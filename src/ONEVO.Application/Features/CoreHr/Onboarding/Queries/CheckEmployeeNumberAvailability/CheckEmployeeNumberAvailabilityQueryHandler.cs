using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.CheckEmployeeNumberAvailability;

public sealed class CheckEmployeeNumberAvailabilityQueryHandler(
    IEmployeeRepository employees,
    ICurrentUser currentUser)
    : IRequestHandler<CheckEmployeeNumberAvailabilityQuery, Result<EmployeeNumberAvailabilityResponse>>
{
    public async Task<Result<EmployeeNumberAvailabilityResponse>> Handle(
        CheckEmployeeNumberAvailabilityQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EmployeeNumberAvailabilityResponse>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<EmployeeNumberAvailabilityResponse>.Forbidden("Tenant context missing.");

        var employeeNumber = EmployeeNumberRules.NormalizeInput(request.EmployeeNumber);
        if (string.IsNullOrEmpty(employeeNumber))
            return Result<EmployeeNumberAvailabilityResponse>.Failure("Employee number is required.");

        if (!EmployeeNumberRules.IsValidFormat(employeeNumber))
            return Result<EmployeeNumberAvailabilityResponse>.Failure(EmployeeNumberRules.InvalidFormatMessage);

        var exists = await employees.EmployeeNumberExistsAsync(tenantId, employeeNumber, excludeId: null, ct);
        return Result<EmployeeNumberAvailabilityResponse>.Success(
            new EmployeeNumberAvailabilityResponse(employeeNumber, Available: !exists));
    }
}
