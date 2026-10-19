using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>A file posted with a chat message. Downloaded only through the conversation's access check.</summary>
public class ConversationMessageAttachment : BaseEntity
{
    public Guid MessageId { get; set; }
    public Guid FileRecordId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
