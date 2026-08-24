using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
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
        IReadOnlyDictionary<string, string?> mapping, ISet<string> emailsSeenInThisFile, CancellationToken ct,
        BulkOnboardingResolutionState? resolutionState = null)
    {
        resolutionState ??= new BulkOnboardingResolutionState();

        string? Get(string field)
        {
            // Prefer synthetic override keys when no column was mapped.
            var synthetic = $"__override_{field}";
            if (rawData.TryGetValue(synthetic, out var overridden) && overridden.Length > 0)
                return overridden;

            return mapping.TryGetValue(field, out var col) && col is not null && rawData.TryGetValue(col, out var v) && v.Length > 0
                ? v
                : null;
        }

        var firstName = Get("firstName");
        var lastName = Get("lastName");
        var workEmail = Get("workEmail");
        var startDateRaw = Get("startDate");
        var employmentType = Get("employmentType") ?? batch.DefaultEmploymentType;
        var employeeNumber = Get("employeeNumber");

        if (string.IsNullOrWhiteSpace(firstName))
            return Invalid(BulkOnboardingIssueTypes.MissingFirstName, "firstName", "First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            return Invalid(BulkOnboardingIssueTypes.MissingLastName, "lastName", "Last name is required.");
        if (string.IsNullOrWhiteSpace(workEmail))
            return Invalid(BulkOnboardingIssueTypes.MissingWorkEmail, "workEmail", "Work email is required.");

        var normalizedEmail = workEmail.Trim().ToLowerInvariant();
        if (emailsSeenInThisFile.Contains(normalizedEmail))
            return Invalid(
                BulkOnboardingIssueTypes.DuplicateWorkEmail, "workEmail",
                $"Duplicate work email '{workEmail}' also appears in an earlier row of this file.", workEmail);
        emailsSeenInThisFile.Add(normalizedEmail);

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, batch.LegalEntityId, workEmail, excludeId: null, ct))
            return Invalid(
                BulkOnboardingIssueTypes.DuplicateWorkEmail, "workEmail",
                $"An employee with the email '{workEmail}' already exists in this company.", workEmail);

        if (!DateOnly.TryParse(startDateRaw, out var startDate))
            return Invalid(
                BulkOnboardingIssueTypes.InvalidStartDate, "startDate",
                "Start date is required and must be a valid date (YYYY-MM-DD).", startDateRaw);

        if (string.IsNullOrWhiteSpace(employmentType))
            return Invalid(
                BulkOnboardingIssueTypes.EmploymentTypeMissing, "employmentType",
                "Employment type is required (set a default for the batch or add an Employment Type column).");

        var employmentTypeMap = BulkOnboardingResolutionStateSerializer.FindValueMap(
            resolutionState, "employmentType", employmentType);
        if (employmentTypeMap?.TargetId is not null &&
            string.Equals(employmentTypeMap.Action, BulkOnboardingIssueTypes.Actions.MapExisting, StringComparison.Ordinal))
        {
            employmentType = employmentTypeMap.NewValue ?? employmentType;
        }
        else if (await _employmentTypeRepository.GetIdByCodeAsync(employmentType, ct) is null)
        {
            return Invalid(
                BulkOnboardingIssueTypes.EmploymentTypeNotFound, "employmentType",
                $"'{employmentType}' is not a known employment type.", employmentType);
        }

        Guid? departmentId = null;
        var departmentName = Get("department");
        if (departmentName is not null)
        {
            var deptMap = BulkOnboardingResolutionStateSerializer.FindValueMap(
                resolutionState, "department", departmentName);
            if (deptMap?.TargetId is not null &&
                Guid.TryParse(deptMap.TargetId, out var mappedDeptId) &&
                string.Equals(deptMap.Action, BulkOnboardingIssueTypes.Actions.MapExisting, StringComparison.Ordinal))
            {
                departmentId = mappedDeptId;
            }
            else
            {
                var lookupName = deptMap?.NewValue ?? departmentName;
                var departments = await _departmentRepository.ListByLegalEntityAsync(
                    tenantId, batch.LegalEntityId, includeInactive: false, ct);
                var match = departments.FirstOrDefault(d =>
                    string.Equals(d.Name, lookupName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Invalid(
                        BulkOnboardingIssueTypes.DepartmentNotFound, "department",
                        $"Department '{lookupName}' was not found. Create it under Organization -> Departments first.",
                        lookupName);
                departmentId = match.Id;
            }
        }

        Guid? positionId = null;
        Position? resolvedPosition = null;
        var positionName = Get("position");
        if (positionName is not null)
        {
            var posMap = BulkOnboardingResolutionStateSerializer.FindValueMap(
                resolutionState, "position", positionName);
            if (posMap?.TargetId is not null &&
                Guid.TryParse(posMap.TargetId, out var mappedPosId) &&
                string.Equals(posMap.Action, BulkOnboardingIssueTypes.Actions.MapExisting, StringComparison.Ordinal))
            {
                positionId = mappedPosId;
                var positionsForMap = await _positionRepository.ListByLegalEntityAsync(
                    tenantId, batch.LegalEntityId, includeInactive: false, departmentId, ct);
                resolvedPosition = positionsForMap.FirstOrDefault(p => p.Id == mappedPosId);
            }
            else
            {
                var lookupName = posMap?.NewValue ?? positionName;
                var positions = await _positionRepository.ListByLegalEntityAsync(
                    tenantId, batch.LegalEntityId, includeInactive: false, departmentId, ct);
                var match = positions.FirstOrDefault(p =>
                    string.Equals(p.Name, lookupName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Invalid(
                        BulkOnboardingIssueTypes.PositionNotFound, "position",
                        $"Position '{lookupName}' was not found in this company/department. Create it under Organization -> Positions first.",
                        lookupName);
                positionId = match.Id;
                resolvedPosition = match;
            }
        }

        Guid? resolvedReportsToEmployeeId = null;
        if (resolvedPosition?.ReportsToPositionId is { } reportsToPositionId)
        {
            var activeHolders = await _positionAssignments.GetActiveHoldersAsync(tenantId, reportsToPositionId, ct);
            if (activeHolders.Count > 1)
            {
                var reportingManagerRaw = Get("reportingManager");
                if (string.IsNullOrWhiteSpace(reportingManagerRaw))
                    return Invalid(
                        BulkOnboardingIssueTypes.ReportingManagerRequired, "reportingManager",
                        "This position reports to a manager role held by more than one person. Choose which employee should be the reporting manager.",
                        relatedEntityId: reportsToPositionId.ToString());

                var matchedHolder = activeHolders.FirstOrDefault(h =>
                    string.Equals(h.WorkEmail, reportingManagerRaw.Trim(), StringComparison.OrdinalIgnoreCase));
                if (matchedHolder is null)
                    return Invalid(
                        BulkOnboardingIssueTypes.ReportingManagerNotFound, "reportingManager",
                        "The reporting manager could not be matched to a current holder of the manager position. Choose an eligible employee.",
                        reportingManagerRaw,
                        reportsToPositionId.ToString());

                resolvedReportsToEmployeeId = matchedHolder.EmployeeId;
            }
        }

        int? workModeId = batch.DefaultWorkModeId;
        var workModeCode = Get("workMode");
        if (workModeCode is not null)
        {
            var workModeMap = BulkOnboardingResolutionStateSerializer.FindValueMap(
                resolutionState, "workMode", workModeCode);
            if (workModeMap?.TargetId is not null &&
                int.TryParse(workModeMap.TargetId, out var mappedWorkModeId) &&
                string.Equals(workModeMap.Action, BulkOnboardingIssueTypes.Actions.MapExisting, StringComparison.Ordinal))
            {
                workModeId = mappedWorkModeId;
            }
            else
            {
                var lookupCode = workModeMap?.NewValue ?? workModeCode;
                var workModes = await _workModeRepository.ListActiveAsync(ct);
                var match = workModes.FirstOrDefault(w =>
                    string.Equals(w.Code, lookupCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(w.Label, lookupCode, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Invalid(
                        BulkOnboardingIssueTypes.WorkModeNotFound, "workMode",
                        $"Work mode '{lookupCode}' is not a known work mode.", lookupCode);
                workModeId = match.Id;
            }
        }
        if (workModeId is null)
            return Invalid(
                BulkOnboardingIssueTypes.WorkModeMissing, "workMode",
                "Work mode is required (set a default for the batch or add a Work Mode column).");

        Guid? templateId = batch.DefaultChecklistTemplateId;
        var templateName = Get("checklistTemplate");
        if (templateName is not null)
        {
            var templateMap = BulkOnboardingResolutionStateSerializer.FindValueMap(
                resolutionState, "checklistTemplate", templateName);
            if (templateMap?.TargetId is not null &&
                Guid.TryParse(templateMap.TargetId, out var mappedTemplateId) &&
                string.Equals(templateMap.Action, BulkOnboardingIssueTypes.Actions.MapExisting, StringComparison.Ordinal))
            {
                templateId = mappedTemplateId;
            }
            else
            {
                var lookupName = templateMap?.NewValue ?? templateName;
                var matches = await _checklistTemplateRepository.ListOnboardingMatchesAsync(
                    tenantId, batch.LegalEntityId, departmentId, positionId, ct);
                var match = matches.FirstOrDefault(t =>
                    string.Equals(t.Template.Name, lookupName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Invalid(
                        BulkOnboardingIssueTypes.ChecklistTemplateNotFound, "checklistTemplate",
                        $"Checklist template '{lookupName}' was not found for this company/department/position.",
                        lookupName);
                templateId = match.Template.Id;
            }
        }

        if (employeeNumber is not null &&
            await _employeeRepository.EmployeeNumberExistsAsync(tenantId, employeeNumber, excludeId: null, ct))
            return Invalid(
                BulkOnboardingIssueTypes.DuplicateEmployeeNumber, "employeeNumber",
                $"Employee number '{employeeNumber}' is already in use.", employeeNumber);

        return new RowValidationOutcome(
            true, null, departmentId, positionId, templateId,
            firstName, lastName, workEmail, startDate, employmentType, workModeId, employeeNumber,
            resolvedReportsToEmployeeId);

        RowValidationOutcome Invalid(
            string code,
            string field,
            string message,
            string? importedValue = null,
            string? relatedEntityId = null) => new(
            false,
            new RowValidationError(code, field, message, importedValue, relatedEntityId),
            null, null, null, firstName ?? string.Empty, lastName ?? string.Empty,
            workEmail ?? string.Empty, null, employmentType ?? string.Empty, null, employeeNumber, null);
    }
}
