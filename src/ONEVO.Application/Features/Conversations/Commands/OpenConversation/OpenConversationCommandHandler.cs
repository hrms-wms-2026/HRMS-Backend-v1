using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.OpenConversation;

public sealed class OpenConversationCommandHandler : IRequestHandler<OpenConversationCommand, Result<ConversationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly ConversationResponseBuilder _responses;

    public OpenConversationCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, ConversationResponseBuilder responses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _responses = responses;
    }

    public async Task<Result<ConversationResponse>> Handle(OpenConversationCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ConversationResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<ConversationResponse>.Forbidden(ConversationRules.NoEmployeeMessage);

        var subject = new ItemRef(command.Source, command.Kind, command.Id);
        var subjectInfo = await _access.ResolveSubjectAsync(tenantId, subject, ct);
        if (subjectInfo is null)
            return Result<ConversationResponse>.NotFound(ConversationRules.NotFoundMessage);

        var conversation = await _conversations.GetBySubjectAsync(tenantId, subject, ct);
        if (conversation is null)
        {
            // Only a base participant may bring a conversation into existence.
            if (!subjectInfo.BaseParticipantEmployeeIds.Contains(caller.Value))
                return Result<ConversationResponse>.NotFound(ConversationRules.NotFoundMessage);
            conversation = await _conversations.GetOrCreateAsync(new Conversation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectSource = subject.Source, SubjectKind = subject.Kind,
                SubjectId = subject.Id, CreatedById = _currentUser.UserId, CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        var context = await _access.AuthorizeAsync(tenantId, conversation, subjectInfo, caller.Value, ct);
        if (context is null)
            return Result<ConversationResponse>.NotFound(ConversationRules.NotFoundMessage);

        if (await EnsureBaseRowsAsync(tenantId, context, ct))
            context = await _access.AuthorizeAsync(tenantId, conversation, subjectInfo, caller.Value, ct) ?? context;

        var page = await _conversations.ListMessagesAsync(tenantId, conversation.Id, before: null, ConversationRules.DefaultPageSize + 1, ct);
        var hasMore = page.Count > ConversationRules.DefaultPageSize;
        var messages = page.Take(ConversationRules.DefaultPageSize).Reverse().ToList();

        return Result<ConversationResponse>.Success(new ConversationResponse(
            conversation.Id, subject.Source, subject.Kind, subject.Id, subjectInfo.Title, subjectInfo.ProjectId,
            await _responses.ParticipantsAsync(tenantId, context, ct),
            await _responses.MessagesAsync(tenantId, messages, ct),
            hasMore,
            context.RowOf(caller.Value)?.LastReadAt));
    }

    /// <summary>Gives each current base participant a row (it holds their read marker). True if any was added.</summary>
    private async Task<bool> EnsureBaseRowsAsync(Guid tenantId, ConversationContext context, CancellationToken ct)
    {
        var inserted = false;
        foreach (var employeeId in context.SubjectInfo.BaseParticipantEmployeeIds.Where(id => context.RowOf(id) is null))
        {
            inserted |= await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = context.Conversation.Id, EmployeeId = employeeId,
                JoinReason = ConversationJoinReasons.Base, AddedAt = DateTimeOffset.UtcNow, CreatedById = _currentUser.UserId
            }, ct);
        }
        return inserted;
    }
}
