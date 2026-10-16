using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.MarkConversationRead;

public sealed class MarkConversationReadCommandHandler : IRequestHandler<MarkConversationReadCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;

    public MarkConversationReadCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkConversationReadCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result.Forbidden(ConversationRules.NoEmployeeMessage);

        var context = await _access.LoadAsync(tenantId, command.ConversationId, caller.Value, ct);
        if (context is null)
            return Result.NotFound(ConversationRules.NotFoundMessage);

        var now = DateTimeOffset.UtcNow;
        if (context.RowOf(caller.Value) is { } row)
        {
            row.LastReadAt = now;
            _conversations.UpdateParticipant(row);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            // A base participant who has not opened the chat since becoming one (e.g. a new position holder).
            await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = context.Conversation.Id, EmployeeId = caller.Value,
                JoinReason = ConversationJoinReasons.Base, AddedAt = now, LastReadAt = now, CreatedById = _currentUser.UserId
            }, ct);
        }
        return Result.Success();
    }
}
