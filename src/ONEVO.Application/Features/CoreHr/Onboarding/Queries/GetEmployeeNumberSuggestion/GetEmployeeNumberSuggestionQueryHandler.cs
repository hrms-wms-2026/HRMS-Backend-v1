using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetEmployeeNumberSuggestion;

public sealed class GetEmployeeNumberSuggestionQueryHandler(
    ILegalEntityRepository legalEntities,
    IEmployeeRepository employees,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeNumberSuggestionQuery, Result<EmployeeNumberSuggestionResponse>>
{
    public async Task<Result<EmployeeNumberSuggestionResponse>> Handle(
        GetEmployeeNumberSuggestionQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EmployeeNumberSuggestionResponse>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<EmployeeNumberSuggestionResponse>.Forbidden("Tenant context missing.");

        if (request.LegalEntityId == Guid.Empty)
            return Result<EmployeeNumberSuggestionResponse>.Failure("A company is required.");

        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<EmployeeNumberSuggestionResponse>.NotFound("Company not found.");

        if (!legalEntity.IsActive)
            return Result<EmployeeNumberSuggestionResponse>.UnprocessableEntity(
                "The selected company is inactive.");

        if (!EmployeeNumberRules.TryNormalizePrefix(legalEntity.CompanyCode, out var prefix, out var prefixError))
            return Result<EmployeeNumberSuggestionResponse>.UnprocessableEntity(prefixError!);

        var sequence = await employees.GetNextEmployeeNumberSequenceAsync(tenantId, prefix, ct);
        var employeeNumber = EmployeeNumberRules.FormatSuggested(prefix, sequence);

        // Extremely unlikely with dense sequences, but skip any collision (e.g. custom numbers
        // outside the padded pattern) without reserving — final save still re-checks.
        var guard = 0;
        while (await employees.EmployeeNumberExistsAsync(tenantId, employeeNumber, excludeId: null, ct)
               && guard < 1000)
        {
            sequence++;
            employeeNumber = EmployeeNumberRules.FormatSuggested(prefix, sequence);
            guard++;
        }

        if (employeeNumber.Length > EmployeeNumberRules.MaxLength)
            return Result<EmployeeNumberSuggestionResponse>.UnprocessableEntity(
                "Could not generate an employee number. Enter one manually.");

        return Result<EmployeeNumberSuggestionResponse>.Success(
            new EmployeeNumberSuggestionResponse(employeeNumber, prefix, sequence));
    }
}
