using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public static class ConversationMessageKinds
{
    public const string Text = "text";
    public const string System = "system";
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
}
