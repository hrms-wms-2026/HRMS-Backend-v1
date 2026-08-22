using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed record LeaveRequestEvaluation(
    Employee TargetEmployee,
    LeaveType LeaveType,
    LeaveEntitlement Entitlement,
    LeavePolicyAggregate Policy,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    decimal CurrentRemaining,
    bool NoticePeriodMissed,
    IReadOnlyList<LeaveRequestWarningResponse> Warnings,
    IReadOnlyList<LeaveRequestCalendarConflict> CalendarConflicts,
    decimal? TeamAbsencePercent,
    LeaveApproverResolution Approvers);

public sealed class LeaveRequestSubmissionEvaluator
{
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyRepository _policies;
    private readonly ILeaveRequestRepository _requests;
    private readonly LeaveRequestDayCalculator _dayCalculator;
    private readonly ILeaveHolidayProvider _holidays;
    private readonly ILeaveApproverResolver _approvers;
    private readonly ILeaveRequestConflictProvider _conflicts;
    private readonly ILeaveTeamAbsenceWarningService _teamAbsence;
    private readonly IDateTimeProvider _clock;
    private readonly LeaveRequestOptions _options;

    public LeaveRequestSubmissionEvaluator(
        IEmployeeRepository employees,
        ILeaveTypeRepository leaveTypes,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyRepository policies,
        ILeaveRequestRepository requests,
        LeaveRequestDayCalculator dayCalculator,
        ILeaveHolidayProvider holidays,
        ILeaveApproverResolver approvers,
        ILeaveRequestConflictProvider conflicts,
        ILeaveTeamAbsenceWarningService teamAbsence,
        IDateTimeProvider clock,
        IOptions<LeaveRequestOptions> options)
    {
        _employees = employees;
        _leaveTypes = leaveTypes;
        _entitlements = entitlements;
        _policies = policies;
        _requests = requests;
        _dayCalculator = dayCalculator;
        _holidays = holidays;
        _approvers = approvers;
        _conflicts = conflicts;
        _teamAbsence = teamAbsence;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<LeaveRequestEvaluation>> EvaluateAsync(
        Guid tenantId,
        Guid requesterUserId,
        Guid? onBehalfEmployeeId,
        Guid leaveTypeId,
        DateOnly startDate,
        DateOnly endDate,
        string? halfDayPeriod,
        string? reason,
        IReadOnlyList<Guid> fileRecordIds,
        CancellationToken ct)
    {
        var requester = await _employees.GetByUserIdAsync(tenantId, requesterUserId, ct);
        if (requester is null)
            return Result<LeaveRequestEvaluation>.NotFound(LeaveRequestMessages.NoEmployeeRecord);

        var target = onBehalfEmployeeId is { } employeeId
            ? await _employees.GetByIdAsync(tenantId, employeeId, ct)
            : requester;
        if (target is null)
            return Result<LeaveRequestEvaluation>.NotFound(LeaveRequestMessages.EmployeeNotFound);

        if (startDate.Year != endDate.Year)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.CrossYear);

        if (!string.IsNullOrWhiteSpace(halfDayPeriod) && startDate != endDate)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.HalfDaySameDay);

        if (startDate < _clock.Today && !_options.AllowBackdatedRequests)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.StartInPast);

        var rangeDays = endDate.DayNumber - startDate.DayNumber + 1;
        if (rangeDays > _options.MaximumRequestRangeDays)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.RangeExceeded);

        if (await _requests.HasOverlappingPendingOrApprovedRequestAsync(tenantId, target.Id, startDate, endDate, ct))
            return Result<LeaveRequestEvaluation>.Conflict(LeaveRequestMessages.Overlap);

        var leaveType = await _leaveTypes.GetByIdAsync(tenantId, leaveTypeId, ct);
        if (leaveType is null || !leaveType.IsActive)
            return Result<LeaveRequestEvaluation>.NotFound("The selected leave type no longer exists.");

        if (leaveType.ApplicableGender is LeaveGenderRestrictions.Male or LeaveGenderRestrictions.Female
            && !string.Equals(target.Gender, leaveType.ApplicableGender, StringComparison.OrdinalIgnoreCase))
        {
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.GenderRestricted);
        }

        if (target.LegalEntityId is not Guid legalEntityId)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.NoEntitlement);

        var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, [legalEntityId], startDate.Year, ct);
        if (!policies.TryGetValue(legalEntityId, out var policy)
            || policy.LeaveTypes.All(t => t.Rule.LeaveTypeId != leaveTypeId))
        {
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.NoEntitlement);
        }

        var entitlement = await _entitlements.GetTrackedByEmployeeTypeYearAsync(
            tenantId, target.Id, leaveTypeId, startDate.Year, ct);
        if (entitlement is null)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.NoEntitlement);

        var assignment = policy.LegalEntities.FirstOrDefault(x => x.Assignment.LegalEntityId == legalEntityId);
        IReadOnlyCollection<int> workingDays;
        try
        {
            workingDays = LegalEntityMapper.ParseStandardWorkingDays(assignment?.StandardWorkingDaysJson ?? "[]");
        }
        catch (Exception)
        {
            workingDays = [];
        }

        var holidays = await _holidays.ListHolidaysAsync(tenantId, legalEntityId, startDate, endDate, ct);
        var calculated = _dayCalculator.Calculate(new LeaveRequestDayCalculationInput(
            startDate, endDate, halfDayPeriod, workingDays, holidays));
        if (calculated.TotalDays <= 0)
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.NoWorkingDays);

        if (policy.Policy.MinDaysPerRequest > 0 && calculated.TotalDays < policy.Policy.MinDaysPerRequest)
        {
            return Result<LeaveRequestEvaluation>.Failure(
                $"Leave request must be at least {policy.Policy.MinDaysPerRequest:0.#} days.");
        }

        if (policy.BlackoutPeriods.Any(p => p.StartDate <= endDate && p.EndDate >= startDate))
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.Blackout);

        if (_options.RequireReason && string.IsNullOrWhiteSpace(reason))
            return Result<LeaveRequestEvaluation>.Failure("A reason is required for this leave request.");

        var documentAfter = leaveType.DocumentRequiredAfterDays;
        var needsDocument = leaveType.RequiresDocument
            || (documentAfter is { } after && calculated.TotalDays > after);
        if (needsDocument && fileRecordIds.Count == 0)
        {
            return Result<LeaveRequestEvaluation>.Failure(
                LeaveRequestMessages.DocumentRequired(leaveType.Name, documentAfter ?? 0));
        }

        if (!await _requests.AreAvailableFileRecordsAsync(tenantId, fileRecordIds, ct))
            return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.FileNotAvailable);

        var expiry = LeaveEntitlementPlanner.CarryExpiryFromPolicy(policy, leaveTypeId, startDate.Year);
        var carry = LeaveEntitlementMapper.EffectiveCarry(entitlement.CarriedForwardDays, expiry, _clock.Today);
        var currentRemaining = LeaveEntitlementMapper.Remaining(
            entitlement.TotalDays, carry, entitlement.UsedDays, entitlement.PendingDays);

        decimal paidDays;
        decimal unpaidDays;
        if (!leaveType.IsPaid)
        {
            paidDays = 0m;
            unpaidDays = calculated.TotalDays;
        }
        else
        {
            paidDays = Math.Min(calculated.TotalDays, Math.Max(0m, currentRemaining));
            unpaidDays = calculated.TotalDays - paidDays;
            if (unpaidDays > 0m && !_options.AllowUnpaidSplitWhenBalanceShort)
            {
                return Result<LeaveRequestEvaluation>.Failure(
                    LeaveRequestMessages.InsufficientBalance(currentRemaining, leaveType.Name));
            }
        }

        var warnings = new List<LeaveRequestWarningResponse>();
        var noticeDays = Math.Max(leaveType.MinimumNoticeDays, policy.Policy.MinimumNoticeDays);
        var noticeMissed = noticeDays > 0 && startDate < _clock.Today.AddDays(noticeDays);
        if (noticeMissed)
            warnings.Add(new LeaveRequestWarningResponse("notice_period_missed", LeaveRequestMessages.NoticeMissed(noticeDays)));

        var maxConsecutive = leaveType.MaxConsecutiveDays ?? policy.Policy.MaxConsecutiveDays;
        if (maxConsecutive is { } limit && calculated.CountedDates.Count > limit)
            warnings.Add(new LeaveRequestWarningResponse("max_consecutive_days", LeaveRequestMessages.MaxConsecutive(limit)));

        LeaveApproverResolution approvers;
        if (leaveType.RequiresApproval)
        {
            approvers = await _approvers.ResolveAsync(tenantId, target.Id, startDate, endDate, ct);
            if (approvers.Approvers.Count == 0)
                return Result<LeaveRequestEvaluation>.Failure(LeaveRequestMessages.NoApprover);
        }
        else
        {
            approvers = new LeaveApproverResolution([]);
        }

        var calendarConflicts = await _conflicts.ListConflictsAsync(tenantId, target.Id, startDate, endDate, ct);
        var teamWarning = await _teamAbsence.BuildWarningAsync(
            tenantId, target.Id, startDate, endDate, policy.Policy.MaxTeamAbsencePercent, ct);
        if (teamWarning is not null)
            warnings.Add(new LeaveRequestWarningResponse("team_absence", teamWarning.Message));

        return Result<LeaveRequestEvaluation>.Success(new LeaveRequestEvaluation(
            target,
            leaveType,
            entitlement,
            policy,
            calculated.TotalDays,
            paidDays,
            unpaidDays,
            currentRemaining,
            noticeMissed,
            warnings,
            calendarConflicts,
            teamWarning?.TeamAbsencePercent,
            approvers));
    }
}
