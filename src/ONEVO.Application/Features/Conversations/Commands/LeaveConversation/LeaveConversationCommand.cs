using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.LeaveConversation;

/// <summary>An added member leaves (at any time). Base participants and viewers cannot leave.</summary>
public sealed record LeaveConversationCommand(Guid ConversationId) : IRequest<Result>;

public sealed class LeaveConversationCommandHandler : IRequestHandler<LeaveConversationCommand, Result>
{
    public const string ParticipantLeftEvent = "participant_left";
    public const string BaseCannotLeave = "You are part of this request, so you cannot leave its conversation.";
    public const string ViewerCannotLeave = "You see this item from your own access, so you cannot leave its conversation.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<LeaveConversationCommandHandler> _logger;

    public LeaveConversationCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IUnitOfWork unitOfWork, IInboxRealtimePublisher realtime, ILogger<LeaveConversationCommandHandler> logger)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result> Handle(LeaveConversationCommand command, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result.Forbidden(ConversationRules.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result.NotFound(ConversationRules.NotFoundMessage);
        if (context.IsBase(caller.EmployeeId))
            return Result.UnprocessableEntity(BaseCannotLeave);
        var row = context.RowOf(caller.EmployeeId);
        if (row is null || row.JoinReason != ConversationJoinReasons.Added)
            return Result.UnprocessableEntity(ViewerCannotLeave);

        var now = DateTimeOffset.UtcNow;
        var name = (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct))
            .GetValueOrDefault(caller.EmployeeId)?.Name ?? "A teammate";
        var system = ConversationSystemMessages.Create(caller.TenantId, context.Conversation.Id, $"{name} left the conversation",
            JsonSerializer.Serialize(new { type = ParticipantLeftEvent, employee = caller.EmployeeId }), now, caller.UserId);

        row.LeftAt = now;
        _conversations.UpdateParticipant(row);
        await _conversations.AddMessageAsync(system, [], ct);
        context.Conversation.LastMessageAt = now;
        _conversations.Update(context.Conversation);
        await _unitOfWork.SaveChangesAsync(ct);

        await ConversationRealtime.AfterDepartureAsync(_realtime, _logger, caller.TenantId, context, caller.EmployeeId, system, ct);
        return Result.Success();
    }
}
