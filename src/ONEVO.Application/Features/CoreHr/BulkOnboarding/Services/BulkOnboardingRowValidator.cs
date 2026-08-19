using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public class BulkOnboardingRowValidator : IBulkOnboardingRowValidator
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IPositionAssignmentRepository _positionAssignments;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly IEmploymentTypeRepository _employmentTypeRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IChecklistTemplateRepository _checklistTemplateRepository;

    public BulkOnboardingRowValidator(
        IDepartmentRepository departmentRepository, IPositionRepository positionRepository,
        IPositionAssignmentRepository positionAssignments,
        IWorkModeRepository workModeRepository, IEmploymentTypeRepository employmentTypeRepository,
        IEmployeeRepository employeeRepository, IChecklistTemplateRepository checklistTemplateRepository)
    {
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _positionAssignments = positionAssignments;
        _workModeRepository = workModeRepository;
        _employmentTypeRepository = employmentTypeRepository;
        _employeeRepository = employeeRepository;
        _checklistTemplateRepository = checklistTemplateRepository;
    }

    public async Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId, BulkOnboardingBatch batch, Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping, ISet<string> emailsSeenInThisFile, CancellationToken ct)
    {
        string? Get(string field) =>
            mapping.TryGetValue(field, out var col) && col is not null && rawData.TryGetValue(col, out var v) && v.Length > 0 ? v : null;

        var firstName = Get("firstName");
        var lastName = Get("lastName");
        var workEmail = Get("workEmail");
        var startDateRaw = Get("startDate");
        var employmentType = Get("employmentType") ?? batch.DefaultEmploymentType;
        var employeeNumber = Get("employeeNumber");

        if (string.IsNullOrWhiteSpace(firstName))
            return Invalid("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            return Invalid("Last name is required.");
        if (string.IsNullOrWhiteSpace(workEmail))
            return Invalid("Work email is required.");

        var normalizedEmail = workEmail.Trim().ToLowerInvariant();
        if (emailsSeenInThisFile.Contains(normalizedEmail))
            return Invalid($"Duplicate work email '{workEmail}' also appears in an earlier row of this file.");
        emailsSeenInThisFile.Add(normalizedEmail);

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, batch.LegalEntityId, workEmail, excludeId: null, ct))
            return Invalid($"An employee with the email '{workEmail}' already exists in this company.");

        if (!DateOnly.TryParse(startDateRaw, out var startDate))
            return Invalid("Start date is required and must be a valid date (YYYY-MM-DD).");

        if (string.IsNullOrWhiteSpace(employmentType))
            return Invalid("Employment type is required (set a default for the batch or add an Employment Type column).");
        if (await _employmentTypeRepository.GetIdByCodeAsync(employmentType, ct) is null)
            return Invalid($"'{employmentType}' is not a known employment type.");

        Guid? departmentId = null;
        var departmentName = Get("department");
        if (departmentName is not null)
        {
            var departments = await _departmentRepository.ListByLegalEntityAsync(tenantId, batch.LegalEntityId, includeInactive: false, ct);
            var match = departments.FirstOrDefault(d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Department '{departmentName}' was not found. Create it under Organization -> Departments first.");
            departmentId = match.Id;
        }

        Guid? positionId = null;
        Position? resolvedPosition = null;
        var positionName = Get("position");
        if (positionName is not null)
        {
            var positions = await _positionRepository.ListByLegalEntityAsync(tenantId, batch.LegalEntityId, includeInactive: false, departmentId, ct);
            var match = positions.FirstOrDefault(p => string.Equals(p.Name, positionName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Position '{positionName}' was not found in this company/department. Create it under Organization -> Positions first.");
            positionId = match.Id;
            resolvedPosition = match;
        }

        Guid? resolvedReportsToEmployeeId = null;
        if (resolvedPosition?.ReportsToPositionId is { } reportsToPositionId)
        {
            var activeHolders = await _positionAssignments.GetActiveHoldersAsync(tenantId, reportsToPositionId, ct);
            if (activeHolders.Count > 1)
            {
                var reportingManagerRaw = Get("reportingManager");
                if (string.IsNullOrWhiteSpace(reportingManagerRaw))
                    return Invalid("Row references a position whose manager position has multiple current holders — a Reporting Manager column value is required.");

                var matchedHolder = activeHolders.FirstOrDefault(h =>
                    string.Equals(h.WorkEmail, reportingManagerRaw.Trim(), StringComparison.OrdinalIgnoreCase));
                if (matchedHolder is null)
                    return Invalid($"Reporting Manager \"{reportingManagerRaw}\" is not a current holder of this position's manager position.");

                resolvedReportsToEmployeeId = matchedHolder.EmployeeId;
            }
        }

        int? workModeId = batch.DefaultWorkModeId;
        var workModeCode = Get("workMode");
        if (workModeCode is not null)
        {
            var workModes = await _workModeRepository.ListActiveAsync(ct);
            var match = workModes.FirstOrDefault(w => string.Equals(w.Code, workModeCode, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Work mode '{workModeCode}' is not a known work mode.");
            workModeId = match.Id;
        }
        if (workModeId is null)
            return Invalid("Work mode is required (set a default for the batch or add a Work Mode column).");

        Guid? templateId = batch.DefaultChecklistTemplateId;
        var templateName = Get("checklistTemplate");
        if (templateName is not null)
        {
            var matches = await _checklistTemplateRepository.ListOnboardingMatchesAsync(tenantId, batch.LegalEntityId, departmentId, positionId, ct);
            var match = matches.FirstOrDefault(t => string.Equals(t.Template.Name, templateName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Checklist template '{templateName}' was not found for this company/department/position.");
            templateId = match.Template.Id;
        }

        if (employeeNumber is not null &&
            await _employeeRepository.EmployeeNumberExistsAsync(tenantId, employeeNumber, excludeId: null, ct))
            return Invalid($"Employee number '{employeeNumber}' is already in use.");

        return new RowValidationOutcome(
            true, null, departmentId, positionId, templateId,
            firstName, lastName, workEmail, startDate, employmentType, workModeId, employeeNumber,
            resolvedReportsToEmployeeId);

        RowValidationOutcome Invalid(string message) => new(
            false, message, null, null, null, firstName ?? string.Empty, lastName ?? string.Empty,
            workEmail ?? string.Empty, null, employmentType ?? string.Empty, null, employeeNumber, null);
    }
}
