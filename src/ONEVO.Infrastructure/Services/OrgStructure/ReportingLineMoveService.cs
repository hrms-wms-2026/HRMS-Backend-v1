using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.OrgStructure;

public sealed class ReportingLineMoveService(
    ApplicationDbContext db,
    IPositionPeopleChartReader chartReader,
    IEmployeeHierarchyClosureRepository hierarchyClosure,
    IDateTimeProvider clock) : IReportingLineMoveService
{
    private const int MaxSelectedEmployees = 100;
    private const int MaxReasonLength = 500;
    private const string NotificationTemplateCode = "reporting_manager_changed";

    public async Task<Result<ReportingLineMoveImpactResponse>> PreviewAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> selectedEmployeeIds,
        Guid newManagerEmployeeId,
        CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(
            tenantId, legalEntityId, selectedEmployeeIds, newManagerEmployeeId, ct);
        return plan.IsSuccess
            ? Result<ReportingLineMoveImpactResponse>.Success(plan.Value!.Impact)
            : Result<ReportingLineMoveImpactResponse>.Failure(
                plan.Error!, plan.StatusCode ?? 400, plan.ErrorCode);
    }

    public async Task<Result<ReportingLineMoveImpactResponse>> MoveAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> selectedEmployeeIds,
        Guid newManagerEmployeeId,
        string? reason,
        CancellationToken ct = default)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > MaxReasonLength)
            return Result<ReportingLineMoveImpactResponse>.UnprocessableEntity(
                $"Reason must be {MaxReasonLength} characters or fewer.");

        var plan = await BuildPlanAsync(
            tenantId, legalEntityId, selectedEmployeeIds, newManagerEmployeeId, ct);
        if (!plan.IsSuccess)
        {
            return Result<ReportingLineMoveImpactResponse>.Failure(
                plan.Error!, plan.StatusCode ?? 400, plan.ErrorCode);
        }

        var newManagers = plan.Value!.NewManagerByEmployeeId;
        var changedIds = newManagers.Keys.ToList();
        var assignments = await (
            from assignment in db.PositionAssignments
            join employee in db.Employees on assignment.EmployeeId equals employee.Id
            where assignment.TenantId == tenantId
                && employee.TenantId == tenantId
                && employee.LegalEntityId == legalEntityId
                && changedIds.Contains(assignment.EmployeeId)
                && assignment.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && assignment.AssignmentStatus == PositionAssignmentStatus.Active
            select assignment)
            .ToListAsync(ct);

        var latestAssignments = assignments
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.EffectiveFrom).First());
        if (latestAssignments.Count != changedIds.Count)
            return Result<ReportingLineMoveImpactResponse>.Conflict(
                "The reporting structure changed while this move was being prepared. Refresh and try again.");

        foreach (var (employeeId, managerId) in newManagers)
            latestAssignments[employeeId].ReportsToEmployeeId = managerId;

        var batchId = Guid.NewGuid();
        var effectiveAt = clock.UtcNow;
        var changes = plan.Value.Impact.Changes;
        await db.ReportingManagerChanges.AddRangeAsync(changes.Select(change => new ReportingManagerChange
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BatchId = batchId,
            LegalEntityId = legalEntityId,
            EmployeeId = change.Employee.EmployeeId,
            PreviousManagerEmployeeId = change.CurrentManager?.EmployeeId,
            NewManagerEmployeeId = change.NewManager.EmployeeId,
            EffectiveAt = effectiveAt,
            ChangeReason = normalizedReason
        }), ct);

        await AddNotificationsAsync(tenantId, batchId, effectiveAt, changes, ct);

        // EnableRetryOnFailure is configured - EF Core forbids a user-initiated BeginTransactionAsync
        // under a retrying execution strategy unless it runs inside ExecuteAsync (it threw, so every
        // move returned 500 before anything was saved).
        var executionStrategy = db.Database.CreateExecutionStrategy();
        try
        {
            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(ct)
                    : null;
                // The source-of-truth updates, immutable history and notifications commit together.
                await db.SaveChangesAsync(ct);
                await hierarchyClosure.RebuildAsync(tenantId, ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ReportingLineMoveImpactResponse>.Conflict(
                "The reporting structure changed while this move was being saved. Refresh and try again.");
        }

        return Result<ReportingLineMoveImpactResponse>.Success(plan.Value.Impact);
    }

    private async Task AddNotificationsAsync(
        Guid tenantId,
        Guid batchId,
        DateTimeOffset createdAt,
        IReadOnlyCollection<ReportingLineMoveChangeResponse> changes,
        CancellationToken ct)
    {
        var recipientEmployeeIds = changes
            .Select(change => change.Employee.EmployeeId)
            .Concat(changes.Where(change => change.CurrentManager is not null)
                .Select(change => change.CurrentManager!.EmployeeId))
            .Concat(changes.Select(change => change.NewManager.EmployeeId))
            .Distinct()
            .ToList();

        var userIdByEmployeeId = await db.Employees
            .AsNoTracking()
            .Where(employee => employee.TenantId == tenantId
                && recipientEmployeeIds.Contains(employee.Id)
                && employee.UserId != Guid.Empty)
            .ToDictionaryAsync(employee => employee.Id, employee => employee.UserId, ct);

        var messagesByUserId = new Dictionary<Guid, List<string>>();
        void AddMessage(Guid employeeId, string message)
        {
            if (!userIdByEmployeeId.TryGetValue(employeeId, out var userId)) return;
            if (!messagesByUserId.TryGetValue(userId, out var messages))
            {
                messages = [];
                messagesByUserId[userId] = messages;
            }
            if (!messages.Contains(message, StringComparer.Ordinal)) messages.Add(message);
        }

        foreach (var change in changes)
        {
            AddMessage(
                change.Employee.EmployeeId,
                $"Your reporting manager changed from {change.CurrentManager?.Name ?? "Top of organisation"} to {change.NewManager.Name}.");
        }

        foreach (var oldManagerGroup in changes
                     .Where(change => change.CurrentManager is not null)
                     .GroupBy(change => change.CurrentManager!.EmployeeId))
        {
            var names = JoinNames(oldManagerGroup.Select(change => change.Employee.Name));
            AddMessage(oldManagerGroup.Key, $"{names} no longer report{(oldManagerGroup.Count() == 1 ? "s" : string.Empty)} directly to you.");
        }

        // People left behind get a different new manager from the people who moved, so each new
        // manager hears only about their own new direct reports.
        foreach (var newManagerGroup in changes.GroupBy(change => change.NewManager.EmployeeId))
        {
            var names = JoinNames(newManagerGroup.Select(change => change.Employee.Name));
            AddMessage(
                newManagerGroup.Key,
                $"{names} now report{(newManagerGroup.Count() == 1 ? "s" : string.Empty)} directly to you.");
        }

        await db.Notifications.AddRangeAsync(messagesByUserId.Select(entry => new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = entry.Key,
            TemplateCode = NotificationTemplateCode,
            Title = "Reporting line updated",
            Body = string.Join(" ", entry.Value),
            RelatedEntityType = "reporting_manager_change",
            RelatedEntityId = batchId,
            CreatedAt = createdAt
        }), ct);
    }

    private static string JoinNames(IEnumerable<string> names)
    {
        var list = names.Distinct(StringComparer.Ordinal).OrderBy(name => name).ToList();
        return list.Count switch
        {
            0 => "The selected employees",
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => $"{string.Join(", ", list.Take(list.Count - 1))}, and {list[^1]}"
        };
    }

    private async Task<Result<MovePlan>> BuildPlanAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> selectedEmployeeIds,
        Guid newManagerEmployeeId,
        CancellationToken ct)
    {
        var selected = selectedEmployeeIds.Where(id => id != Guid.Empty).ToHashSet();
        if (selected.Count == 0)
            return Result<MovePlan>.UnprocessableEntity("Select at least one employee to move.");
        if (selected.Count > MaxSelectedEmployees)
            return Result<MovePlan>.UnprocessableEntity(
                $"A reporting-line move can include at most {MaxSelectedEmployees} selected employees.");
        if (newManagerEmployeeId == Guid.Empty)
            return Result<MovePlan>.UnprocessableEntity("Choose a new reporting manager.");

        var chart = await chartReader.BuildAsync(tenantId, legalEntityId, ct);
        var people = chart
            .Where(n => n.Kind == "person" && !n.IsPending && n.EmployeeId.HasValue)
            .ToDictionary(n => n.EmployeeId!.Value);

        if (!people.TryGetValue(newManagerEmployeeId, out var manager))
            return Result<MovePlan>.NotFound("The selected reporting manager is not an active employee in this company.");

        var missing = selected.Where(id => !people.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            return Result<MovePlan>.UnprocessableEntity(
                "One or more selected people are no longer active in this reporting structure. Refresh and try again.");

        Guid? ParentEmployeeId(Guid employeeId)
        {
            var parentId = people[employeeId].ParentId;
            if (parentId is null || !parentId.StartsWith("e:", StringComparison.Ordinal)) return null;
            return Guid.TryParse(parentId.AsSpan(2), out var parsed) ? parsed : null;
        }

        // The selection is exactly the set of people who move. A selected person keeps reporting to
        // their nearest selected ancestor (so a checked team moves together) and otherwise goes to
        // the new manager. Anyone left unchecked directly under a mover stays put: they report to
        // their nearest ancestor who is not moving.
        if (selected.Contains(newManagerEmployeeId))
            return Result<MovePlan>.UnprocessableEntity(
                "This move would create a reporting cycle. Choose a manager who is not one of the people being moved.");

        Guid? NearestAncestor(Guid employeeId, bool moving)
        {
            var seen = new HashSet<Guid>();
            var cursor = ParentEmployeeId(employeeId);
            while (cursor is Guid ancestorId && seen.Add(ancestorId))
            {
                if (selected.Contains(ancestorId) == moving) return ancestorId;
                cursor = people.ContainsKey(ancestorId) ? ParentEmployeeId(ancestorId) : null;
            }
            return null;
        }

        ReportingLineMovePersonResponse Person(Guid employeeId)
        {
            var node = people[employeeId];
            return new ReportingLineMovePersonResponse(
                employeeId,
                $"{node.FirstName} {node.LastName}".Trim(),
                node.PositionName);
        }

        var newManagerByEmployeeId = new Dictionary<Guid, Guid>();
        foreach (var employeeId in selected)
        {
            var target = NearestAncestor(employeeId, moving: true) ?? manager.EmployeeId!.Value;
            if (ParentEmployeeId(employeeId) != target) newManagerByEmployeeId[employeeId] = target;
        }

        var leftBehindIds = people.Keys
            .Where(id => !selected.Contains(id) && ParentEmployeeId(id) is Guid parentId && selected.Contains(parentId))
            .ToList();
        foreach (var employeeId in leftBehindIds)
        {
            if (NearestAncestor(employeeId, moving: false) is not Guid stayManagerId)
                return Result<MovePlan>.UnprocessableEntity(
                    $"{Person(employeeId).Name} would be left without a manager. Select them too, or choose a different selection.");
            newManagerByEmployeeId[employeeId] = stayManagerId;
        }

        if (newManagerByEmployeeId.Count == 0)
            return Result<MovePlan>.UnprocessableEntity(
                "The selected people already report to this manager.");

        // Final structure must still be a tree.
        Guid? FinalParent(Guid employeeId) =>
            newManagerByEmployeeId.TryGetValue(employeeId, out var moved) ? moved : ParentEmployeeId(employeeId);
        foreach (var employeeId in newManagerByEmployeeId.Keys)
        {
            var seen = new HashSet<Guid> { employeeId };
            for (var cursor = FinalParent(employeeId); cursor is Guid ancestorId; cursor = people.ContainsKey(ancestorId) ? FinalParent(ancestorId) : null)
            {
                if (!seen.Add(ancestorId))
                    return Result<MovePlan>.UnprocessableEntity(
                        "This move would create a reporting cycle. Choose a manager outside the selected people.");
            }
        }

        var changes = newManagerByEmployeeId
            .Select(entry =>
            {
                var currentManagerId = ParentEmployeeId(entry.Key);
                return new ReportingLineMoveChangeResponse(
                    Person(entry.Key),
                    currentManagerId is Guid id && people.ContainsKey(id) ? Person(id) : null,
                    Person(entry.Value));
            })
            .OrderBy(change => change.Employee.Name)
            .ToList();

        var impact = new ReportingLineMoveImpactResponse(
            selected.Count,
            changes.Count,
            selected.Count(id => !newManagerByEmployeeId.ContainsKey(id)),
            selected.Count + leftBehindIds.Count,
            changes);

        return Result<MovePlan>.Success(new MovePlan(newManagerByEmployeeId, impact));
    }

    private sealed record MovePlan(
        IReadOnlyDictionary<Guid, Guid> NewManagerByEmployeeId,
        ReportingLineMoveImpactResponse Impact);
}
