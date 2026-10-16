namespace ONEVO.Application.Features.Inbox.Contracts;

/// <summary>
/// The one bell-navigation rule (plan S4.2): a notification about an inbox item carries
/// RelatedEntityType = "inbox:{source}:{kind}" and RelatedEntityId = the item id, so the bell opens
/// /inbox/{source}/{kind}/{id}. <see cref="ByTemplate"/> lists every module template that is about an inbox item;
/// each module switches its template to <see cref="RelatedEntityType"/> in its own source part (L1, A1, X1, C2).
/// Conversation templates already use the rule (ConversationNotifier). Work Management keeps its existing mappings.
/// </summary>
public static class InboxNotificationNavigation
{
    public const string Prefix = "inbox:";

    public static string RelatedEntityType(string source, string kind) => $"{Prefix}{source}:{kind}";

    public static readonly IReadOnlyDictionary<string, (string Source, string Kind)> ByTemplate =
        new Dictionary<string, (string Source, string Kind)>
        {
            ["leave_request_approved"] = ("leave", "request"),
            ["leave_request_rejected"] = ("leave", "request"),
            ["leave_request_information_requested"] = ("leave", "request"),
            ["leave_request_next_approval_required"] = ("leave", "request"),
            ["leave_request_cancelled_by_employee"] = ("leave", "request"),
            ["leave_request_cancelled_by_hr"] = ("leave", "request"),
            ["leave_request_partially_cancelled"] = ("leave", "request"),
            ["leave_request_decision_undone"] = ("leave", "request"),
            ["attendance_correction_request_created"] = ("attendance", "correction"),
            ["attendance_correction_request_decided"] = ("attendance", "correction"),
            ["attendance_correction_request_cancelled"] = ("attendance", "correction"),
            ["work_area_change_request_created"] = ("attendance", "work_mode_change"),
            ["work_area_change_request_decided"] = ("attendance", "work_mode_change"),
            ["work_area_change_request_cancelled"] = ("attendance", "work_mode_change"),
            ["exception_alert_detected"] = ("monitoring", "exception_case"),
            ["exception_alert_escalated"] = ("monitoring", "exception_case"),
        };

    /// <summary>"inbox:{source}:{kind}" for an inbox template, else null (not about an inbox item).</summary>
    public static string? RelatedEntityTypeFor(string templateCode)
        => ByTemplate.TryGetValue(templateCode, out var target) ? RelatedEntityType(target.Source, target.Kind) : null;
}
