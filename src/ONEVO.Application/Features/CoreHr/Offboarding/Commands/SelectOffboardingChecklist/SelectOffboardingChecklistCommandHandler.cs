using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;

public class SelectOffboardingChecklistCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IChecklistTemplateRepository checklistTemplateRepository,
    IEmployeeChecklistTaskRepository employeeChecklistTaskRepository,
    IEmployeeRepository employeeRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SelectOffboardingChecklistCommand, Result>
{
    public async Task<Result> Handle(SelectOffboardingChecklistCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");
        if (record.Status != OffboardingRecordStatuses.Initiated)
            return Result.Conflict("A checklist has already been selected for this offboarding.");

        var template = await checklistTemplateRepository.GetByIdAsync(tenantId, request.TemplateId, ct);
        if (template is null || !template.IsActive || template.TemplateType != "offboarding")
            return Result.NotFound("The selected checklist template does not exist or is not an active offboarding template.");

        var employee = await employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");
        if (template.LegalEntityId != employee.LegalEntityId)
            return Result.UnprocessableEntity("This template does not belong to the employee's company.");

        var tasks = await employeeChecklistTaskRepository.InstantiateAsync(
            template, employee.Id, employee.UserId, editedTasksJson: null, anchorDate: record.LastWorkingDate, ct);
        foreach (var task in tasks)
            task.OffboardingRecordId = record.Id;

        record.ChecklistTemplateId = template.Id;
        record.Status = OffboardingRecordStatuses.InProgress;
        record.UpdatedAt = clock.UtcNow;

        await offboardingRecordRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
