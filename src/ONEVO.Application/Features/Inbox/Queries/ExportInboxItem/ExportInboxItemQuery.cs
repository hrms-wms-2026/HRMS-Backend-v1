using System.Net;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Queries.ExportInboxItem;

public sealed record InboxExport(string FileName, string Html);

/// <summary>
/// The investigation bundle of one item (plan S5): detail, steps, decision journal, and the full chat including every
/// edit and deleted message (from the audit), plus the attachment list. For deciders and people who see the item from
/// their own scope (HR, escalation targets) - not for people only added to the chat, nor the employee it is about.
/// No PDF service exists in the codebase, so the bundle is a self-contained HTML document.
/// </summary>
public sealed record ExportInboxItemQuery(string Source, string Kind, Guid Id) : IRequest<Result<InboxExport>>;

public sealed class ExportInboxItemQueryHandler : IRequestHandler<ExportInboxItemQuery, Result<InboxExport>>
{
    public const string NotAllowed = "Only the people who decide this item, or who review it from their own access, can export it.";
    private const int MaxMessages = 10_000;

    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IInboxDecisionJournal _journal;
    private readonly ICallerIdentityResolver _identity;
    private readonly IDateTimeProvider _clock;

    public ExportInboxItemQueryHandler(
        InboxCallerResolver caller, IInboxRouter router, IConversationAccess access, IConversationRepository conversations,
        IInboxDecisionJournal journal, ICallerIdentityResolver identity, IDateTimeProvider clock)
    {
        _caller = caller;
        _router = router;
        _access = access;
        _conversations = conversations;
        _journal = journal;
        _identity = identity;
        _clock = clock;
    }

    public async Task<Result<InboxExport>> Handle(ExportInboxItemQuery query, CancellationToken ct)
    {
        if (await _caller.ResolveAsync(ct) is not { } caller)
            return Result<InboxExport>.Forbidden(InboxCallerResolver.NoEmployeeMessage);

        var detail = await _router.GetDetailAsync(caller.TenantId, caller.EmployeeId, query.Source, query.Kind, query.Id, ct);
        if (!detail.IsSuccess)
            return Result<InboxExport>.NotFound(detail.Error!);

        var item = new ItemRef(query.Source, query.Kind, query.Id);
        var subject = await _access.ResolveSubjectAsync(caller.TenantId, item, ct);
        var isDecider = subject is not null && subject.BaseParticipantEmployeeIds.Contains(caller.EmployeeId)
            && subject.SubjectEmployeeId != caller.EmployeeId && detail.Value!.Item.RequestedBy?.Id != caller.EmployeeId;
        if (!isDecider && !await _access.CanViewAsync(caller.TenantId, item, caller.EmployeeId, ct))
            return Result<InboxExport>.Forbidden(NotAllowed);

        var journal = await _journal.ListForItemAsync(caller.TenantId, item, ct);
        var conversation = await _conversations.GetBySubjectAsync(caller.TenantId, item, ct);
        var messages = conversation is null
            ? []
            : (await _conversations.ListMessagesAsync(caller.TenantId, conversation.Id, null, MaxMessages, ct)).Reverse().ToList();
        var ids = messages.Select(m => m.Id).ToList();
        var revisions = await _conversations.ListRevisionsAsync(caller.TenantId, ids, ct);
        var attachments = await _conversations.ListAttachmentsAsync(caller.TenantId, ids, ct);
        var people = messages.Where(m => m.AuthorEmployeeId.HasValue).Select(m => m.AuthorEmployeeId!.Value)
            .Concat(journal.Select(j => j.ActorEmployeeId)).Distinct().ToList();
        var names = people.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, people, ct)).ToDictionary(p => p.Key, p => p.Value.Name);

        var html = InboxInvestigationExport.Render(detail.Value!, journal, messages, revisions, attachments, names, _clock.UtcNow);
        return Result<InboxExport>.Success(new InboxExport($"investigation-{query.Source}-{query.Kind}-{query.Id:N}.html", html));
    }
}

/// <summary>Renders the investigation bundle. Every value is HTML-encoded except chat bodies, which are stored
/// sanitized (ConversationRules rejects active content) and are shown as they appeared in the chat.</summary>
public static class InboxInvestigationExport
{
    public const string DeletedMarker = "[deleted]";
    public const string EditedMarker = "[earlier version]";

    public static string Render(
        InboxItemDetail detail, IReadOnlyList<InboxDecisionJournalEntry> journal, IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<ConversationMessageRevision> revisions, IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>> attachments,
        IReadOnlyDictionary<Guid, string> names, DateTimeOffset generatedAt)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        string Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value, "A teammate") : "System";
        static string At(DateTimeOffset? at) => at?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "";

        var item = detail.Item;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(item.Title)).Append("</title>")
            .Append("<style>body{font-family:sans-serif;max-width:900px;margin:24px auto;color:#222}table{border-collapse:collapse;width:100%}")
            .Append("td,th{border:1px solid #ccc;padding:4px 8px;text-align:left;vertical-align:top}.deleted{color:#a00}.edited{color:#555}")
            .Append(".reverted{text-decoration:line-through}</style></head><body>");
        sb.Append("<h1>").Append(E(item.Title)).Append("</h1>")
            .Append("<p>").Append(E($"{item.Source} / {item.Kind} · {item.Status}")).Append(" · exported ").Append(E(At(generatedAt))).Append("</p>");
        if (!string.IsNullOrWhiteSpace(detail.Summary))
            sb.Append("<p>").Append(E(detail.Summary)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(detail.Explanation))
            sb.Append("<p>").Append(E(detail.Explanation)).Append("</p>");

        sb.Append("<h2>Details</h2><table><tr><th>Field</th><th>Before</th><th>After</th></tr>");
        foreach (var change in detail.ChangeSet)
            sb.Append("<tr><td>").Append(E(change.Label)).Append("</td><td>").Append(E(change.Before)).Append("</td><td>").Append(E(change.After)).Append("</td></tr>");
        foreach (var (key, value) in detail.ExplanationDetails ?? new Dictionary<string, string>())
            sb.Append("<tr><td>").Append(E(key)).Append("</td><td colspan=\"2\">").Append(E(value)).Append("</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Steps</h2><table><tr><th>When</th><th>Step</th><th>By</th><th>Note</th></tr>");
        foreach (var step in detail.Steps)
            sb.Append("<tr class=\"").Append(step.State == InboxStepStates.Reverted ? "reverted" : "").Append("\"><td>").Append(E(At(step.At)))
                .Append("</td><td>").Append(E(step.Label)).Append(" (").Append(E(step.State)).Append(")</td><td>").Append(E(step.Actor?.Name))
                .Append("</td><td>").Append(E(step.Note)).Append("</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Decision journal</h2><table><tr><th>When</th><th>Action</th><th>By</th><th>Comment</th><th>Before</th><th>After</th></tr>");
        foreach (var entry in journal)
            sb.Append("<tr class=\"").Append(entry.RevertedByJournalId is null ? "" : "reverted").Append("\"><td>").Append(E(At(entry.DecidedAt)))
                .Append("</td><td>").Append(E(entry.Action)).Append("</td><td>").Append(E(entry.ActorEmployeeId == Guid.Empty ? "System" : Name(entry.ActorEmployeeId)))
                .Append("</td><td>").Append(E(entry.Comment)).Append("</td><td>").Append(E(entry.BeforeJson)).Append("</td><td>").Append(E(entry.AfterJson))
                .Append("</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Conversation</h2>");
        var byMessage = revisions.GroupBy(r => r.MessageId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.RevisedAt).ToList());
        foreach (var message in messages)
        {
            sb.Append("<div><p><strong>").Append(E(message.AuthorEmployeeId is null ? "System" : Name(message.AuthorEmployeeId))).Append("</strong> · ")
                .Append(E(At(message.CreatedAt)));
            if (message.Tag is not null)
                sb.Append(" · ").Append(E(message.Tag));
            if (message.EditedAt is not null)
                sb.Append(" · edited ").Append(E(At(message.EditedAt)));
            sb.Append("</p>");
            foreach (var revision in byMessage.GetValueOrDefault(message.Id) ?? [])
            {
                var marker = revision.Kind == ConversationRevisionKinds.Delete ? DeletedMarker : EditedMarker;
                sb.Append("<blockquote class=\"").Append(revision.Kind == ConversationRevisionKinds.Delete ? "deleted" : "edited").Append("\">")
                    .Append(E(marker)).Append(' ').Append(E(At(revision.RevisedAt))).Append(": ").Append(revision.BodyHtml).Append("</blockquote>");
            }
            if (!message.IsRetracted)
                sb.Append("<div>").Append(message.BodyHtml).Append("</div>");
            if (attachments.GetValueOrDefault(message.Id) is { Count: > 0 } files)
            {
                sb.Append("<ul>");
                foreach (var file in files)
                    sb.Append("<li>").Append(E(file.FileName)).Append(" (").Append(E(file.ContentType)).Append(", ").Append(file.SizeBytes).Append(" bytes)</li>");
                sb.Append("</ul>");
            }
            sb.Append("</div><hr>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
