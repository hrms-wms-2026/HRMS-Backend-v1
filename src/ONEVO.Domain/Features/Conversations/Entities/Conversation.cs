using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>A chat, addressed by (SubjectSource, SubjectKind, SubjectId). Three shapes share the table: a person-to-person
/// chat (<see cref="ConversationSources.Direct"/>, one per pair), a named group (<see cref="ConversationSources.Group"/>),
/// and the group of one inbox item, e.g. ("leave", "request", requestId) - the dedicated group a case moves to when people
/// are added or it escalates. One conversation per subject per tenant.</summary>
public class Conversation : BaseEntity
{
    public string SubjectSource { get; set; } = string.Empty;
    public string SubjectKind { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
}

/// <summary>The conversation sources that are not an inbox module.</summary>
public static class ConversationSources
{
    /// <summary>A chat between two employees. SubjectId is derived from the pair, so each pair has exactly one.</summary>
    public const string Direct = "direct";
    /// <summary>A named group anyone started. SubjectId is the conversation's own id; the name is its latest
    /// group_created / group_renamed system message.</summary>
    public const string Group = "group";

    public static bool IsPeopleChat(string source) => source is Direct or Group;
}
