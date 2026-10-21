using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public static class ConversationMessageKinds
{
    public const string Text = "text";
    public const string System = "system";
    /// <summary>A content-free marker where the item's evidence (e.g. an identity case's face checks) sits in the
    /// chat. The client fills it from the item's own evidence, which only reviewers receive - the row holds nothing.</summary>
    public const string Evidence = "evidence";
    /// <summary>An inbox item (request, alert, case) posted into a people chat. SystemEventJson holds its reference
    /// ({"source","kind","id","title",...}); the client loads the item itself, so each viewer sees only what they may.</summary>
    public const string Case = "case";
}

/// <summary>Labels on a text message: justification is the subject employee's own explanation on an alert or case;
/// forwarded is a copy of a message someone passed on from another chat.</summary>
public static class ConversationMessageTags
{
    public const string Justification = "justification";
    public const string Forwarded = "forwarded";
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
    /// <summary>The message this one replies to (same conversation), or null.</summary>
    public Guid? ReplyToMessageId { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    /// <summary>The author deleted the message (BaseEntity.DeletedAt is the time). The row is kept - IsDeleted stays
    /// false - so the chat shows a "message deleted" placeholder; the original body lives in the revisions.</summary>
    public Guid? DeletedByEmployeeId { get; set; }

    public bool IsRetracted => DeletedAt is not null;
}
