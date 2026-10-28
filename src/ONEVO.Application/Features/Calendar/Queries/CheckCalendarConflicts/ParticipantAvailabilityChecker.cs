using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

/// <summary>Leave, holiday and working-hours warnings for a set of people and one meeting time.</summary>
public interface IParticipantAvailabilityChecker
{
    Task<IReadOnlyList<CalendarConflict>> CheckAsync(
        Guid tenantId, Guid? callerEmployeeId, IReadOnlyList<Employee> participants,
        DateTimeOffset start, DateTimeOffset end, bool isAllDay, string requestTimeZone, CancellationToken ct);
}

/// <summary>
/// Loads what <see cref="ParticipantAvailabilityRules"/> needs, once per request rather than per person: approved
/// leave, holidays, legal entities and the caller's leave visibility scope (meeting availability warnings,
/// 2026-10-28 design 4.2 and 5.3). A leave the caller may not see reads "Busy"; the caller's own always reads
/// "On leave".
/// </summary>
public sealed class ParticipantAvailabilityChecker(
    ILeaveRequestReadRepository leave,
    ILeaveCalendarHolidayProvider holidays,
    ILegalEntityRepository legalEntities,
    ICalendarTimezoneResolver timezones,
    ILeaveVisibilityScopeProvider leaveScope,
    IPositionAssignmentRepository positions) : IParticipantAvailabilityChecker
{
    public async Task<IReadOnlyList<CalendarConflict>> CheckAsync(
        Guid tenantId, Guid? callerEmployeeId, IReadOnlyList<Employee> participants,
        DateTimeOffset start, DateTimeOffset end, bool isAllDay, string requestTimeZone, CancellationToken ct)
    {
        if (participants.Count == 0 || end <= start)
            return [];

        var ids = participants.Select(p => p.Id).Distinct().ToList();
        // ±1 day covers every participant's local dates whatever their offset.
        var from = DateOnly.FromDateTime(start.UtcDateTime).AddDays(-1);
        var to = DateOnly.FromDateTime(end.UtcDateTime).AddDays(1);

        var approved = await leave.ListApprovedCoveringAsync(tenantId, ids, from, to, ct);

        var entityIds = participants.Where(p => p.LegalEntityId is not null).Select(p => p.LegalEntityId!.Value).Distinct().ToList();
        var entities = new Dictionary<Guid, LegalEntity>();
        foreach (var id in entityIds)
        {
            if (await legalEntities.GetByIdForTenantAsync(tenantId, id, ct) is { } entity)
                entities[id] = entity;
        }
        var holidayRows = entityIds.Count == 0 ? [] : await holidays.ListHolidaysAsync(tenantId, entityIds, from, to, ct);

        var scope = approved.Count == 0 ? null : (await leaveScope.ResolveForCurrentUserAsync(ct)).Scope;
        var primaryPositions = approved.Count == 0 || scope is null
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>()
            : await positions.GetActivePrimaryByEmployeeIdsAsync(tenantId, ids, ct);

        var fallbackZone = FindZone(requestTimeZone) ?? TimeZoneInfo.Utc;
        var rows = new List<CalendarConflict>();
        foreach (var person in participants.DistinctBy(p => p.Id))
        {
            var zone = person.UserId == Guid.Empty
                ? fallbackZone
                : FindZone(await timezones.ResolveForUserAsync(tenantId, person.UserId, ct)) ?? fallbackZone;
            var entity = person.LegalEntityId is Guid le && entities.TryGetValue(le, out var e) ? e : null;
            primaryPositions.TryGetValue(person.Id, out var primary);
            var leaveVisible = person.Id == callerEmployeeId
                || (scope is not null && EmployeeVisibilityScopeMatcher.Includes(
                    scope, person.Id, primary?.PositionId, person.DepartmentId, person.LegalEntityId));

            rows.AddRange(ParticipantAvailabilityRules.Evaluate(
                new AvailabilityParticipant(person.Id, $"{person.FirstName} {person.LastName}", zone, entity, leaveVisible),
                start, end, isAllDay, approved, holidayRows));
        }

        return rows;
    }

    private static TimeZoneInfo? FindZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
}
