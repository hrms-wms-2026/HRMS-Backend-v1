namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

/// <summary>
/// The stable `code` extension on /meetings ProblemDetails (spec 7). 404 = does not exist or not visible (one body);
/// 403 = visible but not allowed; 400 = validation; 409 = state or concurrency; 503 = feature off.
/// </summary>
public static class MeetingErrorCodes
{
    // Create (400 unless noted)
    public const string TitleInvalid = "meeting_title_invalid";
    public const string PlatformInvalid = "meeting_platform_invalid";
    public const string GuestEmailInvalid = "meeting_guest_email_invalid";
    public const string InvalidTime = "meeting_invalid_time";
    public const string StartInPast = "meeting_start_in_past";
    public const string InvalidTimezone = "meeting_invalid_timezone";
    public const string RecurrenceInvalid = "meeting_recurrence_invalid";
    public const string ModuleMemberRequired = "meeting_module_member_required";   // 403
    public const string CalendarWriteRequired = "meeting_calendar_write_required"; // 403, Office meetings
    public const string ModuleUnavailable = "meeting_module_unavailable";
    public const string OfficeNotReady = "meeting_office_not_ready";               // 409, triggers a reconcile
    public const string ProviderNotConnected = "meeting_provider_not_connected";   // 409
    public const string MeetingsDisabled = "meetings_disabled";                    // 503
    public const string MeetingsReadOnly = "meetings_read_only";                   // 409
    public const string BulkInviteUnavailable = "bulk_invite_unavailable";         // 409

    // Approval (409)
    public const string ApprovalAlreadyDecided = "approval_already_decided";
    public const string ApprovalNotEditable = "meeting_approval_not_editable";

    // Edit (409 unless noted)
    public const string OrganizerRequired = "meeting_organizer_required";          // 403: an attendee tried an organizer action
    public const string OrganizerCannotBeRemoved = "meeting_organizer_cannot_be_removed"; // 400
    public const string ResponseInvalid = "meeting_response_invalid";              // 400
    public const string Changed = "meeting_changed";
    public const string OccurrenceStarted = "meeting_occurrence_started";
    public const string ScopeInvalid = "meeting_scope_invalid";                    // 400
    public const string RecurrenceEmpty = "meeting_recurrence_empty";
    public const string Busy = "meeting_busy";

    // Move
    public const string NothingToMove = "meeting_nothing_to_move";                // 409
    public const string SameModule = "meeting_same_module";                        // 400

    // Link (409)
    public const string LinkExists = "meeting_link_exists";                        // POST without regenerate: true
    public const string LinkInProgress = "meeting_link_in_progress";               // provisioning right now
    public const string LinkStatusUnknown = "meeting_link_status_unknown";         // provisioning_unknown: retry first
    public const string LinkUnresolved = "meeting_link_unresolved";                // confirm the cleanup first
    public const string LinkNotRetryable = "meeting_link_not_retryable";           // retry outside provisioning_unknown/failed
    public const string LinkNotUnresolved = "meeting_link_not_unresolved";         // cleanup-confirmed outside unresolved
    public const string MeetingCancelled = "meeting_cancelled";                    // a link for a cancelled series

    // Read
    public const string NotFound = "meeting_not_found";                            // 404
}
