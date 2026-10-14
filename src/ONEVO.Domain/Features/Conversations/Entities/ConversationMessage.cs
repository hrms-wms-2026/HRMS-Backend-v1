using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public static class ConversationMessageKinds
{
    public const string Text = "text";
    public const string System = "system";
}

/// <summary>Labels on a text message. info_request/info_response are posted by request workflows (leave
/// request-info); justification is the subject employee's own explanation on an alert or case.</summary>
public static class ConversationMessageTags
{
    public const string InfoRequest = "info_request";
    public const string InfoResponse = "info_response";
    public const string Justification = "justification";
}

public class ConversationMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    /// <summary>Null for system messages.</summary>
    public Guid? AuthorEmployeeId { get; set; }
    public string Kind { get; set; } = ConversationMessageKinds.Text;
    public string BodyHtml { get; set; } = string.Empty;
    /// <summary>For system messages, e.g. {"type":"participant_added","by":"…","employee":"…"}.</summary>
    public string? SystemEventJson { get; set; }
    /// <summary><see cref="ConversationMessageTags"/> or null.</summary>
    public string? Tag { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    /// <summary>The author deleted the message (BaseEntity.DeletedAt is the time). The row is kept - IsDeleted stays
    /// false - so the chat shows a "message deleted" placeholder; the original body lives in the revisions.</summary>
    public Guid? DeletedByEmployeeId { get; set; }

    public bool IsRetracted => DeletedAt is not null;
}
