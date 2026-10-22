using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

public static class ExceptionInboxKinds
{
    public const string Source = "monitoring";
    public const string Case = "exception_case";
    public const string Renderer = "exception-evidence";

    public static readonly InboxKindDescriptor Descriptor = new(
        Source, Case, "Exception case", InboxCategories.Alert,
        InboxCapabilities.Chat | InboxCapabilities.Resolve | InboxCapabilities.Escalate | InboxCapabilities.Revert
        | InboxCapabilities.SubjectParticipates,
        Renderer);

    /// <summary>Calendar meeting types live in the calendar source (plan C2/C3), not here.</summary>
    public static bool IsCalendarType(ExceptionType type) => type.ToString().StartsWith("Meeting", StringComparison.Ordinal);
}

public static class ExceptionCaseInboxActions
{
    public const string Acknowledge = "acknowledge";
    public const string Resolve = "resolve";
    public const string Escalate = "escalate";
}

/// <summary>Someone a monitoring case may be escalated to by hand: they hold an exception permission (so the case is
/// theirs to work once it is held), with <see cref="IsDefault"/> marking who the policy would pick. Position and photo
/// are for the picker.</summary>
public sealed record ExceptionEscalationCandidate(
    Guid EmployeeId, Guid UserId, string Name, string Role, bool IsDefault, string? Position = null, Guid? AvatarFileId = null);

/// <summary>A case's review state, kept in the inbox journal so a decision on it can be undone exactly.</summary>
public sealed record ExceptionStateSnapshot(
    string Title, string Status, DateTimeOffset? AcknowledgedAt, Guid? AcknowledgedById, DateTimeOffset? ResolvedAt, Guid? ResolvedById,
    DateTimeOffset? EscalatedAt, Guid? EscalatedById, string? ResolutionNote, string? Outcome)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ExceptionStateSnapshot Of(MonitoringException e) => new(
        e.Title, e.Status.ToString(), e.AcknowledgedAt, e.AcknowledgedById, e.ResolvedAt, e.ResolvedById,
        e.EscalatedAt, e.EscalatedById, e.ResolutionNote, e.Outcome);

    public void ApplyTo(MonitoringException e)
    {
        e.Status = Enum.Parse<ExceptionStatus>(Status);
        e.AcknowledgedAt = AcknowledgedAt;
        e.AcknowledgedById = AcknowledgedById;
        e.ResolvedAt = ResolvedAt;
        e.ResolvedById = ResolvedById;
        e.EscalatedAt = EscalatedAt;
        e.EscalatedById = EscalatedById;
        e.ResolutionNote = ResolutionNote;
        e.Outcome = Outcome;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ExceptionStateSnapshot? Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ExceptionStateSnapshot>(json, Json);
}

/// <summary>
/// Acknowledge / resolve (excused or confirmed) / escalate on an exception case, for the monitoring and calendar inbox
/// sources (plan X1, C3). Each runs the module's own command - which checks the caller's exception scope - and records
/// the journal row in the same transaction; an escalation is recorded the standard way (journal, the new reviewers join
/// the chat as viewers, a system message) with the target from the case type's escalation policy.
/// </summary>
public sealed class ExceptionCaseActions
{
    private const string UnknownAction = "This action is not available for this item.";

    private readonly ISender _sender;
    private readonly IExceptionRepository _exceptions;
    private readonly IInboxDecisionJournal _journal;
    private readonly IInboxEscalationRecorder _escalations;
    private readonly IInboxEscalationPolicyRegistry _policies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationDispatcher? _notifications;
    private readonly IPermissionRepository? _permissions;
    private readonly ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository? _employees;
    private readonly ONEVO.Application.Features.WorkManagement.Common.Services.ICallerIdentityResolver? _identity;

    public ExceptionCaseActions(
        ISender sender, IExceptionRepository exceptions, IInboxDecisionJournal journal, IInboxEscalationRecorder escalations,
        IInboxEscalationPolicyRegistry policies, IUnitOfWork unitOfWork, IDateTimeProvider clock,
        INotificationDispatcher? notifications = null, IPermissionRepository? permissions = null,
        ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository? employees = null,
        ONEVO.Application.Features.WorkManagement.Common.Services.ICallerIdentityResolver? identity = null)
    {
        _identity = identity;
        _permissions = permissions;
        _employees = employees;
        _sender = sender;
        _exceptions = exceptions;
        _journal = journal;
        _escalations = escalations;
        _policies = policies;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _notifications = notifications;
    }

    public async Task<Result<InboxActionResult>> RunAsync(
        Guid tenantId, ItemRef item, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        var note = BodyString(body, "comment");
        var outcome = BodyString(body, "outcome");
        var pickedId = action == ExceptionCaseInboxActions.Escalate && Guid.TryParse(BodyString(body, "targetEmployeeId"), out var parsed)
            ? parsed : (Guid?)null;
        ExceptionEscalationCandidate? picked = null;
        Func<CancellationToken, Task<Result>>? run = action switch
        {
            ExceptionCaseInboxActions.Acknowledge => t => _sender.Send(new AcknowledgeExceptionCommand(item.Id), t),
            ExceptionCaseInboxActions.Resolve => t => _sender.Send(new ResolveExceptionCommand(item.Id, note, outcome), t),
            ExceptionCaseInboxActions.Escalate => t => _sender.Send(new EscalateExceptionCommand(item.Id, note, picked is null ? null : [picked.UserId]), t),
            _ => null,
        };
        if (run is null)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        if (action == ExceptionCaseInboxActions.Resolve && outcome is not null && !ExceptionOutcomes.IsValid(outcome))
            return Result<InboxActionResult>.UnprocessableEntity("The outcome must be excused or confirmed.");

        var exception = await _exceptions.GetByIdAsync(tenantId, item.Id, ct);
        if (exception is null)
            return Result<InboxActionResult>.NotFound("Exception not found.");
        var before = ExceptionStateSnapshot.Of(exception).ToJson();
        if (pickedId is { } targetId)
        {
            picked = (await EscalationCandidatesAsync(tenantId, item, exception, callerEmployeeId, ct)).FirstOrDefault(c => c.EmployeeId == targetId);
            if (picked is null)
                return Result<InboxActionResult>.UnprocessableEntity("That person cannot take this case.");
        }

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async t =>
            {
                var result = await run(t);
                if (!result.IsSuccess)
                    throw new ModuleRefused(Result<InboxActionResult>.Failure(result.Error!, result.StatusCode ?? 400));
                var updated = (await _exceptions.GetByIdAsync(tenantId, item.Id, t))!;
                var after = ExceptionStateSnapshot.Of(updated).ToJson();
                IReadOnlyCollection<Guid> affected = [updated.EmployeeId, callerEmployeeId];
                if (action == ExceptionCaseInboxActions.Escalate)
                {
                    var target = picked is null
                        ? await NextReviewersAsync(tenantId, item, updated, callerEmployeeId, t)
                        : new EscalationTarget.Employees([new EscalationRecipient(picked.UserId, picked.EmployeeId)]);
                    // A calendar case goes up the manager chain (log only at the top) and the next reviewer is told here - the
                    // monitoring alert router never handles calendar types (plan C3).
                    if (item.Source == "calendar" && target is EscalationTarget.Employees next && _notifications is not null)
                        foreach (var recipient in next.Recipients)
                            await _notifications.SendTemplatedAsync(tenantId, recipient.UserId, "calendar_exception_escalated",
                                new Dictionary<string, string> { ["title"] = updated.Title, ["description"] = updated.Description },
                                InboxNotificationNavigation.RelatedEntityType("calendar", "meeting_exception"), updated.Id, t);
                    affected = (await _escalations.RecordAsync(tenantId,
                        new EscalationRecord(item, target, callerEmployeeId, note, [callerEmployeeId], before, after, updated.EmployeeId), t))
                        .Append(updated.EmployeeId).Distinct().ToList();
                }
                else
                {
                    var now = _clock.UtcNow;
                    await _journal.RecordAsync(new InboxDecisionJournalEntry
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, Source = item.Source, Kind = item.Kind, ItemId = item.Id,
                        Action = action == ExceptionCaseInboxActions.Resolve
                            ? InboxJournalActions.Resolve(updated.Outcome ?? ExceptionOutcomes.Confirmed)
                            : InboxJournalActions.Acknowledge,
                        ActorEmployeeId = callerEmployeeId, BeforeJson = before, AfterJson = after,
                        Comment = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), DecidedAt = now, CreatedAt = now,
                    }, t);
                }
                return Result<InboxActionResult>.Success(new InboxActionResult(affected));
            }, ct);
        }
        catch (ModuleRefused refused)
        {
            _unitOfWork.ClearTracking();
            return refused.Result;
        }
    }

    /// <summary>Who the case goes to next under its type's policy (HR for monitoring; the manager chain for calendar).</summary>
    public async Task<EscalationTarget> NextReviewersAsync(
        Guid tenantId, ItemRef item, MonitoringException exception, Guid? currentHolder, CancellationToken ct)
    {
        var policy = _policies.For(item.Source, exception.Type.ToString());
        if (policy is null)
            return new EscalationTarget.LogOnly("No escalation policy for this case type.");
        return await policy.NextAsync(tenantId, new EscalationContext(item, exception.Type.ToString(),
            currentHolder is { } holder ? [holder] : [], exception.EmployeeId, null, null), ct);
    }

    /// <summary>
    /// Who the caller may hand a monitoring case to: everyone holding an exception permission (HR, attendance approvers,
    /// exceptions:acknowledge) - the case they are escalated becomes theirs to work (ExceptionScope.HeldCaseIds) - minus the
    /// subject and the caller. The policy's pick comes first; calendar cases follow their own chain and offer no choice.
    /// </summary>
    public async Task<IReadOnlyList<ExceptionEscalationCandidate>> EscalationCandidatesAsync(
        Guid tenantId, ItemRef item, MonitoringException exception, Guid callerEmployeeId, CancellationToken ct)
    {
        if (item.Source != ExceptionInboxKinds.Source || _permissions is null || _employees is null)
            return [];
        var now = _clock.UtcNow;
        var hr = new HashSet<Guid>();
        foreach (var code in new[] { ExceptionPermissions.HrReview, ExceptionPermissions.HrManage })
            hr.UnionWith(await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, code, now, ct));
        var userIds = new HashSet<Guid>(hr);
        foreach (var code in new[] { ExceptionPermissions.ManagerReview, ExceptionPermissions.Acknowledge })
            userIds.UnionWith(await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, code, now, ct));

        var defaults = await NextReviewersAsync(tenantId, item, exception, callerEmployeeId, ct) is EscalationTarget.Employees next
            ? next.Recipients.Select(r => r.UserId).ToHashSet()
            : [];
        var candidates = new List<ExceptionEscalationCandidate>();
        foreach (var userId in userIds)
        {
            // Only someone with an employee record can hold the case in the inbox.
            if (await _employees.GetDefaultForUserAsync(tenantId, userId, ct) is not { } employee
                || employee.Id == exception.EmployeeId || employee.Id == callerEmployeeId)
                continue;
            candidates.Add(new ExceptionEscalationCandidate(employee.Id, userId, $"{employee.FirstName} {employee.LastName}".Trim(),
                hr.Contains(userId) ? "HR" : "Manager", defaults.Contains(userId)));
        }
        var ids = candidates.Select(c => c.EmployeeId).ToList();
        var positions = await _employees.ListPositionNamesAsync(tenantId, ids, ct);
        var identities = _identity is null || ids.Count == 0
            ? new Dictionary<Guid, ONEVO.Application.Features.WorkManagement.Common.Services.EmployeeIdentityDto>()
            : await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, ids, ct);
        return candidates
            .Select(c => c with
            {
                Position = positions.GetValueOrDefault(c.EmployeeId),
                AvatarFileId = identities.GetValueOrDefault(c.EmployeeId)?.AvatarFileId,
            })
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Role == "HR" ? 0 : 1).ThenBy(c => c.Name).ToList();
    }

    private sealed class ModuleRefused(Result<InboxActionResult> result) : System.Exception(result.Error)
    {
        public Result<InboxActionResult> Result { get; } = result;
    }

    private static string? BodyString(JsonElement? body, string property)
        => body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
