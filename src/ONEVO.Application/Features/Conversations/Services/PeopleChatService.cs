using System.Net;
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Creates the people chats: the one 1-on-1 chat of a pair, and named groups.</summary>
public interface IPeopleChatService
{
    /// <summary>The pair's chat, created (with both people as base members) when missing.</summary>
    Task<Conversation> GetOrCreateDirectAsync(Guid tenantId, Guid a, Guid b, CancellationToken ct);

    /// <summary>A new group: everyone (creator included) is an added member; its name is the group_created line.</summary>
    Task<Conversation> CreateGroupAsync(
        Guid tenantId, Guid creatorEmployeeId, string creatorName, string name, IReadOnlyCollection<Guid> memberIds, CancellationToken ct);

    /// <summary>Writes a group_renamed line (the latest one is the name).</summary>
    Task<ConversationMessage> RenameGroupAsync(Guid tenantId, Conversation group, Guid byEmployeeId, string byName, string name, CancellationToken ct);
}

public sealed class PeopleChatService : IPeopleChatService
{
    private readonly IConversationRepository _conversations;
    private readonly IDateTimeProvider _clock;

    public PeopleChatService(IConversationRepository conversations, IDateTimeProvider clock)
    {
        _conversations = conversations;
        _clock = clock;
    }

    public async Task<Conversation> GetOrCreateDirectAsync(Guid tenantId, Guid a, Guid b, CancellationToken ct)
    {
        var subject = new ItemRef(ConversationSources.Direct, ConversationSources.Direct, PeopleChatNames.DirectSubjectId(a, b));
        var now = _clock.UtcNow;
        var conversation = await _conversations.GetBySubjectAsync(tenantId, subject, ct)
            ?? await _conversations.GetOrCreateAsync(new Conversation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectSource = subject.Source, SubjectKind = subject.Kind, SubjectId = subject.Id,
                CreatedAt = now,
            }, ct);
        foreach (var employeeId in new[] { a, b }.Distinct())
        {
            await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, EmployeeId = employeeId,
                JoinReason = ConversationJoinReasons.Base, AddedAt = now, CreatedAt = now,
            }, ct);
        }
        return conversation;
    }

    public async Task<Conversation> CreateGroupAsync(
        Guid tenantId, Guid creatorEmployeeId, string creatorName, string name, IReadOnlyCollection<Guid> memberIds, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var id = Guid.NewGuid();
        var group = await _conversations.GetOrCreateAsync(new Conversation
        {
            Id = id, TenantId = tenantId, SubjectSource = ConversationSources.Group, SubjectKind = ConversationSources.Group, SubjectId = id,
            CreatedAt = now, LastMessageAt = now,
        }, ct);
        foreach (var employeeId in memberIds.Prepend(creatorEmployeeId).Where(m => m != Guid.Empty).Distinct())
        {
            await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = group.Id, EmployeeId = employeeId,
                JoinReason = ConversationJoinReasons.Added, AddedByEmployeeId = creatorEmployeeId, AddedAt = now, CreatedAt = now,
            }, ct);
        }
        await _conversations.AddMessageIfMissingAsync(NameLine(tenantId, group.Id, PeopleChatEvents.GroupCreated, creatorEmployeeId,
            $"{creatorName} created the group “{name}”", name, now), ct);
        return group;
    }

    public async Task<ConversationMessage> RenameGroupAsync(
        Guid tenantId, Conversation group, Guid byEmployeeId, string byName, string name, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var line = NameLine(tenantId, group.Id, PeopleChatEvents.GroupRenamed, byEmployeeId, $"{byName} renamed the group to “{name}”", name, now);
        await _conversations.AddMessageIfMissingAsync(line, ct);
        await _conversations.TouchAsync(tenantId, group.Id, now, ct);
        return line;
    }

    private static ConversationMessage NameLine(
        Guid tenantId, Guid conversationId, string type, Guid by, string body, string name, DateTimeOffset at)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, Kind = ConversationMessageKinds.System,
            BodyHtml = WebUtility.HtmlEncode(body), SystemEventJson = JsonSerializer.Serialize(new { type, by, name }), CreatedAt = at,
        };
}
