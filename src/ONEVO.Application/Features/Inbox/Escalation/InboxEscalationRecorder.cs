using System.Net;
using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Escalation;

/// <summary>An escalation the module has already applied (status, its own notifications), to be recorded the
/// standard way. <see cref="ActorEmployeeId"/> is null for the SLA sweep. <see cref="SubjectEmployeeId"/> (the person the
/// case is about): when set and the case has no group yet, it goes to that person's 1-on-1 chat with each new reviewer
/// instead of starting a group.</summary>
public sealed record EscalationRecord(
    ItemRef Item, EscalationTarget Target, Guid? ActorEmployeeId, string? Note,
    IReadOnlyCollection<Guid> PreviousHolderEmployeeIds, string? BeforeJson = null, string? AfterJson = null,
    Guid? SubjectEmployeeId = null);

/// <summary>
/// The inbox half of every escalation (plan S2.4), whichever module owns the item: a journal row, the case moves from
/// the 1-on-1 chat to its own group (created when missing, with the people already on it - or, with a SubjectEmployeeId
/// and no group yet, to the subject's 1-on-1 chat with each new reviewer), the new reviewers join that
/// group as viewers, and a system message says who it went to ("Escalated to X", or that nobody is above and it was
/// logged) - an escalation line, never "X added Y". Runs inside the provider's transaction; returns who must refresh their inbox
/// (previous holders + new reviewers) for the provider's <see cref="InboxActionResult"/>.
/// </summary>
public interface IInboxEscalationRecorder
{
    Task<IReadOnlyCollection<Guid>> RecordAsync(Guid tenantId, EscalationRecord record, CancellationToken ct);
}

public sealed class InboxEscalationRecorder : IInboxEscalationRecorder
{
    public const string EscalatedEvent = "escalated";
    public const string LoggedOnlyBody = "Escalated - no one above, logged";

    private readonly IInboxDecisionJournal _journal;
    private readonly IConversationRepository _conversations;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IServiceProvider? _services;

    // The case group service is resolved when needed, not injected: it reaches every module's conversation resolver,
    // and some of those (Work) depend on providers that depend on this recorder - a constructor cycle.
    public InboxEscalationRecorder(
        IInboxDecisionJournal journal, IConversationRepository conversations, ICallerIdentityResolver identity,
        IUnitOfWork unitOfWork, IDateTimeProvider clock, IServiceProvider? services = null)
    {
        _services = services;
        _journal = journal;
        _conversations = conversations;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<Guid>> RecordAsync(Guid tenantId, EscalationRecord record, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var targets = record.Target is EscalationTarget.Employees employees
            ? employees.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).Distinct().ToList()
            : [];

        await _journal.RecordAsync(new InboxDecisionJournalEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Source = record.Item.Source, Kind = record.Item.Kind, ItemId = record.Item.Id,
            Action = InboxJournalActions.Escalate, ActorEmployeeId = record.ActorEmployeeId ?? Guid.Empty,
            BeforeJson = record.BeforeJson,
            AfterJson = record.AfterJson ?? JsonSerializer.Serialize(new
            {
                escalatedTo = targets,
                loggedOnly = (record.Target as EscalationTarget.LogOnly)?.Reason,
            }),
            Comment = string.IsNullOrWhiteSpace(record.Note) ? null : record.Note.Trim(), DecidedAt = now, CreatedAt = now,
        }, ct);

        var caseGroups = _services?.GetService(typeof(ICaseGroupService)) as ICaseGroupService;
        // No group yet: the case moves to the requester's 1-on-1 chat with the new reviewer, not to a new group.
        if (record.SubjectEmployeeId is { } subjectId && targets.Count > 0 && caseGroups is not null
            && await caseGroups.FindAsync(tenantId, record.Item, ct) is null)
        {
            await caseGroups.PlaceEscalationInDirectChatsAsync(tenantId, record.Item, subjectId, targets, record.ActorEmployeeId, record.Note, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return record.PreviousHolderEmployeeIds.Concat(targets).Append(subjectId).Distinct().ToList();
        }

        var conversation = (caseGroups is null ? null : await caseGroups.EnsureAsync(tenantId, record.Item, record.ActorEmployeeId, ct))
            ?? await _conversations.GetBySubjectAsync(tenantId, record.Item, ct)
            ?? await _conversations.GetOrCreateAsync(new Conversation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectSource = record.Item.Source, SubjectKind = record.Item.Kind,
                SubjectId = record.Item.Id, CreatedAt = now,
            }, ct);

        foreach (var employeeId in targets)
        {
            await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, EmployeeId = employeeId,
                JoinReason = ConversationJoinReasons.Viewer, AddedByEmployeeId = record.ActorEmployeeId, AddedAt = now, CreatedAt = now,
            }, ct);
        }

        var names = targets.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, targets, ct)).ToDictionary(p => p.Key, p => p.Value.Name);
        var body = targets.Count == 0
            ? LoggedOnlyBody
            : "Escalated to " + string.Join(", ", targets.Select(id => names.GetValueOrDefault(id, "a reviewer")));
        await _conversations.AddMessageAsync(new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, AuthorEmployeeId = null,
            Kind = ConversationMessageKinds.System, BodyHtml = WebUtility.HtmlEncode(body),
            SystemEventJson = JsonSerializer.Serialize(new { type = EscalatedEvent, by = record.ActorEmployeeId, to = targets }),
            CreatedAt = now,
        }, [], ct);
        conversation.LastMessageAt = now;
        _conversations.Update(conversation);
        await _unitOfWork.SaveChangesAsync(ct);

        return record.PreviousHolderEmployeeIds.Concat(targets).Distinct().ToList();
    }
}
