using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingChecklistMatches;

public class ListOffboardingChecklistMatchesQueryHandler(
    IEmployeeRepository employeeRepository,
    IPositionAssignmentRepository positionAssignmentRepository,
    IChecklistTemplateRepository checklistTemplateRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListOffboardingChecklistMatchesQuery, Result<IReadOnlyList<ChecklistTemplateMatchResponse>>>
{
    public async Task<Result<IReadOnlyList<ChecklistTemplateMatchResponse>>> Handle(
        ListOffboardingChecklistMatchesQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employee = await employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.NotFound("The employee could not be found.");
        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.UnprocessableEntity("This employee has no assigned legal entity.");

        var activeAssignment = await positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);

        var matches = await checklistTemplateRepository.ListOffboardingMatchesAsync(
            tenantId, legalEntityId, employee.DepartmentId, activeAssignment?.PositionId, ct);

        return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.Success(
            matches.Select(m => new ChecklistTemplateMatchResponse(m.Template.Id, m.Template.Name, m.MatchLevel)).ToList());
    }
}
