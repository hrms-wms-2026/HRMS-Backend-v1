using System.Net;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>
/// Posts a message into an item's chat on behalf of a module workflow - e.g. a leave request-info question
/// (tag info_request) or the employee's answer (info_response) - so the chat stays the single thread (plan D-d).
/// The author is the person who took the workflow action; the module has already authorized it. Adds to the unit of
/// work (the caller saves, inside its own transaction) and returns the realtime step, to run after the commit.
/// </summary>
public interface IConversationPoster
{
    Task<Func<Task>> PostAsync(Guid tenantId, ItemRef subject, Guid authorEmployeeId, string text, string? tag, CancellationToken ct);
}

public sealed class ConversationPoster : IConversationPoster
{
    private readonly IConversationRepository _conversations;
    private readonly IConversationAccess _access;
    private readonly ConversationResponseBuilder _responses;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<ConversationPoster> _logger;

    public ConversationPoster(
        IConversationRepository conversations, IConversationAccess access, ConversationResponseBuilder responses,
        IInboxRealtimePublisher realtime, ILogger<ConversationPoster> logger)
    {
        _conversations = conversations;
        _access = access;
        _responses = responses;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Func<Task>> PostAsync(Guid tenantId, ItemRef subject, Guid authorEmployeeId, string text, string? tag, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = await _conversations.GetBySubjectAsync(tenantId, subject, ct)
            ?? await _conversations.GetOrCreateAsync(new Conversation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectSource = subject.Source, SubjectKind = subject.Kind,
                SubjectId = subject.Id, CreatedAt = now
            }, ct);
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, AuthorEmployeeId = authorEmployeeId,
            Kind = ConversationMessageKinds.Text, BodyHtml = $"<p>{WebUtility.HtmlEncode(text.Trim())}</p>", Tag = tag, CreatedAt = now
        };
        await _conversations.AddMessageAsync(message, [], ct);
        conversation.LastMessageAt = now;
        _conversations.Update(conversation);
        // Realtime to the current base members once the caller has committed.
        var info = await _access.ResolveSubjectAsync(tenantId, subject, ct);
        if (info is null)
            return () => Task.CompletedTask;
        var response = (await _responses.MessagesAsync(tenantId, [message], ct))[0];
        return () => ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageCreatedAsync(tenantId, info.BaseParticipantEmployeeIds, conversation.Id, response, CancellationToken.None),
            _logger, "messageCreated");
    }
}
