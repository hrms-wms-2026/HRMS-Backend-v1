namespace ONEVO.Application.Features.Conversations.Validation;

/// <summary>Request-size bounds shared by the conversation and inbox chat validators (task 19). Column sizes come from
/// ConversationConfigurations; the list caps are abuse bounds well above what the UI sends.</summary>
public static class ConversationInputLimits
{
    /// <summary>conversations.subject_source and subject_kind are nvarchar(40).</summary>
    public const int SubjectPartMaxLength = 40;
    /// <summary>conversation_messages.tag is nvarchar(20).</summary>
    public const int TagMaxLength = 20;
    /// <summary>Most people one request may add to, mention in, or start a chat with.</summary>
    public const int MaxPeoplePerRequest = 250;
    /// <summary>Most chats one forward may target.</summary>
    public const int MaxForwardTargets = 20;
}
