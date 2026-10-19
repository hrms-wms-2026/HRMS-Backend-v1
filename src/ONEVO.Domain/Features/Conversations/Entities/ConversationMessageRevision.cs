using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public static class ConversationRevisionKinds
{
    public const string Edit = "edit";
    public const string Delete = "delete";
}

/// <summary>The audit of a message: the body as it was before each edit, and the full body at deletion. The chat
/// is investigation evidence, so this is never shown in the chat - only in the investigation export.</summary>
public class ConversationMessageRevision : BaseEntity
{
    public Guid MessageId { get; set; }
    public string BodyHtml { get; set; } = string.Empty;
    public DateTimeOffset RevisedAt { get; set; }
    public Guid RevisedByEmployeeId { get; set; }
    public string Kind { get; set; } = ConversationRevisionKinds.Edit;
}
