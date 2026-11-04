using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    private const string MonitoringSource = "monitoring";
    private const string ExceptionCaseKind = "exception_case";

    private enum CaseOutcome { Excused, Confirmed, Acknowledged, Escalated, Open, EscalatedToTop }

    private static readonly ExceptionType[] CaseTypes =
    [
        ExceptionType.SustainedLowActivity,
        ExceptionType.AttendanceIrregularity,
        ExceptionType.UnusualActivityPattern,
        ExceptionType.IdentityAnomaly
    ];

    private sealed record CaseText(string Title, string Description, string Ask, string Justification, object Metadata);

    private static CaseText TextFor(ExceptionType type, DateOnly date) => type switch
    {
        ExceptionType.SustainedLowActivity => new(
            "Sustained low activity",
            "Active time stayed under 45% of the workday for three working days in a row.",
            "the agent shows very low activity for the last three days",
            "I was mostly in whiteboard sessions and reviewing printed specs with the team, so the laptop sat idle.",
            new { days = 3, activePercentage = 41, threshold = 45, endDate = date }),
        ExceptionType.AttendanceIrregularity => new(
            "Attendance irregularity",
            "Clocked in late on three of the last five working days.",
            "you were late on three of the last five days",
            "The bus route from Wellawatte changed this month; I've started leaving 30 minutes earlier.",
            new { lateDays = 3, windowDays = 5, averageLateMinutes = 24, endDate = date }),
        ExceptionType.UnusualActivityPattern => new(
            "Unusual activity pattern",
            "Activity continued well past the scheduled end of day with very high input intensity.",
            "there was heavy activity late into the evening",
            "We had a production hotfix that evening - the release notes are in the project channel.",
            new { lastActivityLocal = "22:40", scheduledEnd = "18:00", intensity = 92, date }),
        _ => new(
            "Attendance identity verification requires review",
            "Face verification retries were exhausted during clock-in.",
            "the face check at clock-in failed and was overridden",
            "The office lighting near the entrance was off that morning; the camera couldn't see me properly.",
            new { purpose = "clock_in", outcome = "overridden", similarityScore = 64 }),
    };

    private static readonly Dictionary<CaseOutcome, string> ReviewerClosing = new()
    {
        [CaseOutcome.Excused] = "Thanks for explaining - that makes sense. Marking this as excused.",
        [CaseOutcome.Confirmed] = "I checked the records and this is confirmed. Let's make sure it doesn't repeat next month.",
        [CaseOutcome.Acknowledged] = "Got it, thanks. I'll keep an eye on it this week before closing.",
    };

    /// <summary>
    /// Three cases per person over the window (the owner never gets one - nobody reviews them), each in a different
    /// state: excused, confirmed, acknowledged, still open, escalated one level, or escalated up to the owner. Every case
    /// sits in the 1-on-1 chat between the person and their reviewer (the same route the app uses), with the reviewer's
    /// question and the person's justification; escalated ones move into the case's own group with an "Escalated to"
    /// line, and every decision is written to the inbox journal (the item's Steps and History).
    /// </summary>
    private static async Task SeedExceptionCasesAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Db;
        var workdays = ctx.Window.PastWorkdays();
        if (workdays.Count < 10)
            return;

        var state = await ChatState.LoadAsync(db, ct);
        var people = ctx.Employees.ToDictionary(e => e.Id);
        var recent = workdays.TakeLast(4).ToList();

        for (var index = 0; index < ctx.Employees.Count; index++)
        {
            var subject = ctx.Employees[index];
            if (subject.Id == ctx.Owner.Id)
                continue;
            var reviewerId = await ResolveCaseReviewerAsync(ctx, subject, ct);
            if (!people.TryGetValue(reviewerId, out var reviewer))
                continue;

            for (var k = 0; k < 3; k++)
            {
                var outcome = (CaseOutcome)((index + k * 2) % 6);
                var type = CaseTypes[(index + k) % CaseTypes.Length];
                var date = outcome == CaseOutcome.Open
                    ? recent[(index + k) % recent.Count]
                    : workdays[(6 + index * 4 + k * 13) % (workdays.Count - 4)];
                if (type == ExceptionType.IdentityAnomaly)
                    date = workdays.FirstOrDefault(d => IsFailedCheckDay(subject.Id, d) && d >= date, date);

                var escalateTo = ctx.ManagerOrOwner(reviewer.Id);
                if ((outcome is CaseOutcome.Escalated or CaseOutcome.EscalatedToTop)
                    && (escalateTo == subject.Id || !people.ContainsKey(escalateTo) || reviewer.Id == ctx.Owner.Id))
                    outcome = CaseOutcome.Excused;

                await SeedCaseAsync(ctx, state, people, subject, reviewer, type, date, outcome, escalateTo, ct);
            }

            if ((index + 1) % 4 == 0)
                await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The person's exception reviewer (attendance:approve on their reporting line), else their manager.</summary>
    private static async Task<Guid> ResolveCaseReviewerAsync(SeedContext ctx, Employee subject, CancellationToken ct)
    {
        if (ctx.Authority is not null)
        {
            var route = await ctx.Authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                subject.Id, DapiLegalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct);
            if (route.IsSuccess && route.Value!.ApproverEmployeeId != subject.Id)
                return route.Value.ApproverEmployeeId;
        }
        return ctx.ManagerOrOwner(subject.Id);
    }

    private static async Task SeedCaseAsync(
        SeedContext ctx, ChatState state, IReadOnlyDictionary<Guid, Employee> people, Employee subject, Employee reviewer,
        ExceptionType type, DateOnly date, CaseOutcome outcome, Guid escalateToId, CancellationToken ct)
    {
        var db = ctx.Db;
        var text = TextFor(type, date);
        var caseId = type == ExceptionType.IdentityAnomaly
            ? SeedId($"verification-exception:{subject.Id:N}:{date:yyyy-MM-dd}")
            : SeedId($"exception:{type}:{subject.Id:N}:{date:yyyy-MM-dd}");
        if (state.ExistingCaseIds.Contains(caseId) || await db.Exceptions.AnyAsync(e => e.Id == caseId, ct))
            return;
        state.ExistingCaseIds.Add(caseId);

        var item = new ItemRef(MonitoringSource, ExceptionCaseKind, caseId);
        var detectedAt = type == ExceptionType.IdentityAnomaly ? At(date, 9, 2) : At(date, 19, 30);
        var askedAt = detectedAt.AddHours(type == ExceptionType.IdentityAnomaly ? 1 : 14);
        var justifiedAt = askedAt.AddMinutes(35 + Pick($"j:{caseId:N}", 60));
        var decidedAt = justifiedAt.AddHours(2);
        var escalatedTo = people.GetValueOrDefault(escalateToId);
        var topId = escalatedTo is null ? ctx.Owner.Id : ctx.ManagerOrOwner(escalatedTo.Id);
        var top = outcome == CaseOutcome.EscalatedToTop && escalatedTo is not null && escalatedTo.Id != ctx.Owner.Id
            ? people.GetValueOrDefault(topId)
            : null;

        var exception = new MonitoringException
        {
            Id = caseId,
            TenantId = DapiTenantId,
            EmployeeId = subject.Id,
            Type = type,
            Status = ExceptionStatus.Open,
            Title = text.Title,
            Description = text.Description,
            MetadataJson = JsonSerializer.Serialize(text.Metadata),
            DetectedAt = detectedAt,
        };
        db.Exceptions.Add(exception);
        AddCaseNotification(db, subject.Id, type, caseId, text.Title, detectedAt, read: outcome != CaseOutcome.Open);

        // The 1-on-1 chat between the person and their reviewer carries the case card.
        var direct = await state.DirectChatAsync(db, subject.Id, reviewer.Id, detectedAt, ct);
        AddMessage(db, state, CaseCards.CardId(direct.SubjectId, item), direct, null, ConversationMessageKinds.Case,
            WebUtility.HtmlEncode(text.Title), detectedAt.AddMinutes(1), CaseCards.CardJson(item, text.Title));
        if (outcome == CaseOutcome.Open)
            return;

        AddText(db, state, $"ask:{caseId:N}", direct, reviewer.Id,
            $"Hi {subject.FirstName}, the system flagged that {text.Ask}. Can you tell me what happened?", askedAt);
        AddText(db, state, $"justify:{caseId:N}", direct, subject.Id, text.Justification, justifiedAt,
            ConversationMessageTags.Justification);

        switch (outcome)
        {
            case CaseOutcome.Excused:
            case CaseOutcome.Confirmed:
                Journal(db, item, InboxJournalActions.Acknowledge, reviewer.Id, askedAt.AddMinutes(-5), null);
                AddText(db, state, $"close:{caseId:N}", direct, reviewer.Id, ReviewerClosing[outcome], decidedAt);
                var verdict = outcome == CaseOutcome.Excused ? ExceptionOutcomes.Excused : ExceptionOutcomes.Confirmed;
                Journal(db, item, InboxJournalActions.Resolve(verdict), reviewer.Id, decidedAt.AddMinutes(1), ReviewerClosing[outcome],
                    new { status = "resolved", outcome = verdict });
                exception.Status = ExceptionStatus.Resolved;
                exception.AcknowledgedAt = askedAt.AddMinutes(-5);
                exception.AcknowledgedById = reviewer.UserId;
                exception.ResolvedAt = decidedAt.AddMinutes(1);
                exception.ResolvedById = reviewer.UserId;
                exception.Outcome = verdict;
                exception.ResolutionNote = ReviewerClosing[outcome];
                break;

            case CaseOutcome.Acknowledged:
                Journal(db, item, InboxJournalActions.Acknowledge, reviewer.Id, decidedAt, null);
                AddText(db, state, $"ack:{caseId:N}", direct, reviewer.Id, ReviewerClosing[outcome], decidedAt);
                exception.Status = ExceptionStatus.Acknowledged;
                exception.AcknowledgedAt = decidedAt;
                exception.AcknowledgedById = reviewer.UserId;
                break;

            default:
                await EscalateAsync(ctx, state, item, text.Title, exception, subject, reviewer, escalatedTo!, top, direct, decidedAt, ct);
                break;
        }
    }

    /// <summary>Escalated: the case moves from the 1-on-1 chat into its own group (marker, card, the people on it), the
    /// new reviewer joins as a viewer under an "Escalated to" line, and the journal records it - the same shape
    /// InboxEscalationRecorder writes. EscalatedToTop goes one level further, to the next manager up (the owner).</summary>
    private static async Task EscalateAsync(
        SeedContext ctx, ChatState state, ItemRef item, string title, MonitoringException exception, Employee subject,
        Employee reviewer, Employee target, Employee? top, Conversation direct, DateTimeOffset at, CancellationToken ct)
    {
        var db = ctx.Db;
        var note = $"{subject.FirstName} has had this flagged before - escalating for a second opinion.";
        AddText(db, state, $"escalate-note:{item.Id:N}", direct, reviewer.Id,
            $"Thanks {subject.FirstName}. I'm escalating this to {target.FirstName} so we look at it together.", at.AddMinutes(-10));

        var group = await state.CaseGroupAsync(db, item, at, ct);
        state.AddParticipant(db, group.Id, subject.Id, ConversationJoinReasons.Base, null, at);
        state.AddParticipant(db, group.Id, reviewer.Id, ConversationJoinReasons.Base, null, at);
        AddMessage(db, state, PeopleChatNames.DeterministicId($"case-group:{group.Id:N}"), group, null, ConversationMessageKinds.System,
            WebUtility.HtmlEncode($"Group started for {title}"), at,
            JsonSerializer.Serialize(new { type = PeopleChatEvents.CaseGroup, by = reviewer.Id, source = item.Source, kind = item.Kind, id = item.Id, title }));
        AddMessage(db, state, CaseCards.CardId(group.Id, item), group, null, ConversationMessageKinds.Case,
            WebUtility.HtmlEncode(title), at.AddTicks(1), CaseCards.CardJson(item, title));
        AddMessage(db, state, PeopleChatNames.DeterministicId($"case-moved:{direct.Id:N}:{CaseCards.Key(item)}"), direct, null,
            ConversationMessageKinds.System, WebUtility.HtmlEncode($"Continued in a group: {title}"), at,
            JsonSerializer.Serialize(new { type = PeopleChatEvents.CaseMoved, conversationId = group.Id, source = item.Source, kind = item.Kind, id = item.Id, title }));

        EscalationStep(db, state, item, group, reviewer, target, note, at.AddMinutes(1));
        AddText(db, state, $"target-reply:{item.Id:N}", group, target.Id,
            $"Thanks {reviewer.FirstName}. {subject.FirstName}, let's have a quick call tomorrow morning to go through it.", at.AddHours(3));
        AddText(db, state, $"subject-reply:{item.Id:N}", group, subject.Id, "Sure, I'm free after the stand-up.", at.AddHours(3).AddMinutes(20));

        exception.Status = ExceptionStatus.Escalated;
        exception.AcknowledgedAt = at.AddHours(-2);
        exception.AcknowledgedById = reviewer.UserId;
        exception.EscalatedAt = at.AddMinutes(1);
        exception.EscalatedById = reviewer.UserId;
        exception.ResolutionNote = note;

        if (top is not null)
        {
            var upAt = at.AddDays(1).AddHours(1);
            var upNote = "Needs a decision above my level - it touches the attendance policy.";
            EscalationStep(db, state, item, group, target, top, upNote, upAt);
            AddText(db, state, $"top-reply:{item.Id:N}", group, top.Id,
                "Thanks for flagging. I'll review it with HR this week and come back to you both.", upAt.AddHours(2));
            exception.EscalatedAt = upAt;
            exception.EscalatedById = target.UserId;
            exception.ResolutionNote = upNote;
        }
    }

    private static void EscalationStep(
        ApplicationDbContext db, ChatState state, ItemRef item, Conversation group, Employee by, Employee to, string note,
        DateTimeOffset at)
    {
        state.AddParticipant(db, group.Id, to.Id, ConversationJoinReasons.Viewer, by.Id, at);
        AddMessage(db, state, SeedId($"escalated-line:{item.Id:N}:{to.Id:N}"), group, null, ConversationMessageKinds.System,
            WebUtility.HtmlEncode($"Escalated to {to.FirstName} {to.LastName}"), at,
            JsonSerializer.Serialize(new { type = PeopleChatEvents.Escalated, by = by.Id, to = new[] { to.Id } }));
        Journal(db, item, InboxJournalActions.Escalate, by.Id, at, note, new { escalatedTo = new[] { to.Id }, loggedOnly = (string?)null },
            key: $"escalate:{to.Id:N}");
    }

    private static void Journal(
        ApplicationDbContext db, ItemRef item, string action, Guid actorEmployeeId, DateTimeOffset at, string? comment,
        object? after = null, string? key = null)
    {
        db.InboxDecisionJournal.Add(new InboxDecisionJournalEntry
        {
            Id = SeedId($"journal:{item.Id:N}:{key ?? action}"),
            TenantId = DapiTenantId,
            Source = item.Source,
            Kind = item.Kind,
            ItemId = item.Id,
            Action = action,
            ActorEmployeeId = actorEmployeeId,
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            Comment = comment,
            DecidedAt = at,
            CreatedAt = at
        });
    }

    private static void AddCaseNotification(
        ApplicationDbContext db, Guid employeeId, ExceptionType type, Guid caseId, string title, DateTimeOffset at, bool read)
    {
        var id = type == ExceptionType.IdentityAnomaly
            ? SeedId($"monitoring-alert:{employeeId:N}:{DateOnly.FromDateTime(at.ToOffset(ColomboOffset).DateTime):yyyy-MM-dd}")
            : SeedId($"case-alert:{caseId:N}");
        db.MonitoringNotifications.Add(new Notification
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Type = type == ExceptionType.IdentityAnomaly ? NotificationType.IdentityVerificationAlert : NotificationType.LowActivityAlert,
            Title = title,
            Message = "Your manager has been asked to review this. You can add an explanation in the chat.",
            MetadataJson = JsonSerializer.Serialize(new { reason = type.ToString(), exceptionId = caseId }),
            CreatedAt = at,
            DeliveredToTrayAt = at.AddMinutes(1),
            ReadAt = read ? at.AddHours(1) : null
        });
    }

    private static void AddText(
        ApplicationDbContext db, ChatState state, string key, Conversation conversation, Guid authorEmployeeId, string text,
        DateTimeOffset at, string? tag = null) =>
        AddMessage(db, state, SeedId($"chat:{key}"), conversation, authorEmployeeId, ConversationMessageKinds.Text,
            $"<p>{WebUtility.HtmlEncode(text)}</p>", at, null, tag);

    private static void AddMessage(
        ApplicationDbContext db, ChatState state, Guid id, Conversation conversation, Guid? authorEmployeeId, string kind,
        string bodyHtml, DateTimeOffset at, string? systemEventJson, string? tag = null)
    {
        if (!state.MessageIds.Add(id))
            return;
        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = id,
            TenantId = DapiTenantId,
            ConversationId = conversation.Id,
            AuthorEmployeeId = authorEmployeeId,
            Kind = kind,
            BodyHtml = bodyHtml,
            SystemEventJson = systemEventJson,
            Tag = tag,
            CreatedAt = at
        });
        // The chat list orders by this; it only ever moves forward.
        if (conversation.LastMessageAt is null || at > conversation.LastMessageAt)
            conversation.LastMessageAt = at;
    }

    /// <summary>The conversations, members and message ids already there, so every write is a no-op on re-run.</summary>
    private sealed class ChatState
    {
        private readonly Dictionary<(string Source, string Kind, Guid SubjectId), Conversation> _bySubject = new();
        private readonly HashSet<(Guid ConversationId, Guid EmployeeId)> _members = new();

        public HashSet<Guid> MessageIds { get; private set; } = new();
        public HashSet<Guid> ExistingCaseIds { get; } = new();

        public static async Task<ChatState> LoadAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var state = new ChatState();
            var conversations = await db.Conversations.Where(c => c.TenantId == DapiTenantId).ToListAsync(ct);
            foreach (var c in conversations)
                state._bySubject[(c.SubjectSource, c.SubjectKind, c.SubjectId)] = c;
            var members = await db.ConversationParticipants
                .Where(p => p.TenantId == DapiTenantId)
                .Select(p => new { p.ConversationId, p.EmployeeId })
                .ToListAsync(ct);
            foreach (var m in members)
                state._members.Add((m.ConversationId, m.EmployeeId));
            state.MessageIds = await ExistingIdsAsync(
                db.ConversationMessages.Where(m => m.TenantId == DapiTenantId).Select(m => m.Id), ct);
            return state;
        }

        public async Task<Conversation> DirectChatAsync(ApplicationDbContext db, Guid a, Guid b, DateTimeOffset at, CancellationToken ct)
        {
            var subjectId = PeopleChatNames.DirectSubjectId(a, b);
            var conversation = Get(db, ConversationSources.Direct, ConversationSources.Direct, subjectId, $"direct-chat:{subjectId:N}", at);
            AddParticipant(db, conversation.Id, a, ConversationJoinReasons.Base, null, at);
            AddParticipant(db, conversation.Id, b, ConversationJoinReasons.Base, null, at);
            await Task.CompletedTask;
            return conversation;
        }

        public Task<Conversation> CaseGroupAsync(ApplicationDbContext db, ItemRef item, DateTimeOffset at, CancellationToken ct) =>
            Task.FromResult(Get(db, item.Source, item.Kind, item.Id, $"case-group-chat:{item.Id:N}", at));

        private Conversation Get(ApplicationDbContext db, string source, string kind, Guid subjectId, string idKey, DateTimeOffset at)
        {
            if (_bySubject.TryGetValue((source, kind, subjectId), out var existing))
                return existing;
            var conversation = new Conversation
            {
                Id = SeedId(idKey),
                TenantId = DapiTenantId,
                SubjectSource = source,
                SubjectKind = kind,
                SubjectId = subjectId,
                CreatedAt = at
            };
            db.Conversations.Add(conversation);
            _bySubject[(source, kind, subjectId)] = conversation;
            return conversation;
        }

        public void AddParticipant(ApplicationDbContext db, Guid conversationId, Guid employeeId, string reason, Guid? addedBy, DateTimeOffset at)
        {
            if (!_members.Add((conversationId, employeeId)))
                return;
            db.ConversationParticipants.Add(new ConversationParticipant
            {
                Id = SeedId($"chat-member:{conversationId:N}:{employeeId:N}"),
                TenantId = DapiTenantId,
                ConversationId = conversationId,
                EmployeeId = employeeId,
                JoinReason = reason,
                AddedByEmployeeId = addedBy,
                AddedAt = at,
                // Read up to two days ago: the latest messages show as unread, older ones do not.
                LastReadAt = At(DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColomboOffset).DateTime).AddDays(-2), 18, 0),
                CreatedAt = at
            });
        }

    }
}
