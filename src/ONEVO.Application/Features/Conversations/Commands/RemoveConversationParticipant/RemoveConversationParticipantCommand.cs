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

namespace ONEVO.Application.Features.Conversations.Commands.RemoveConversationParticipant;

/// <summary>Removes an added member. Allowed for base participants and for whoever added that member. Base
/// participants and viewers cannot be removed. The removed person loses access at once; their messages stay.</summary>
public sealed record RemoveConversationParticipantCommand(Guid ConversationId, Guid EmployeeId) : IRequest<Result>;

public sealed class RemoveConversationParticipantCommandHandler : IRequestHandler<RemoveConversationParticipantCommand, Result>
{
    public const string ParticipantRemovedEvent = "participant_removed";
    public const string CannotRemoveBase = "People who are part of this item cannot be removed.";
    public const string CannotRemoveViewer = "This person sees the item from their own access and cannot be removed.";
    public const string NotAllowed = "Only people who are part of this item, or whoever added this person, can remove them.";
    public const string NotInConversation = "This person is not in the conversation.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<RemoveConversationParticipantCommandHandler> _logger;

    public RemoveConversationParticipantCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IUnitOfWork unitOfWork, IInboxRealtimePublisher realtime, ILogger<RemoveConversationParticipantCommandHandler> logger)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result> Handle(RemoveConversationParticipantCommand command, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result.Forbidden(ConversationRules.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result.NotFound(ConversationRules.NotFoundMessage);

        if (context.IsBase(command.EmployeeId))
            return Result.UnprocessableEntity(CannotRemoveBase);
        var row = context.RowOf(command.EmployeeId);
        if (row is null)
            return Result.NotFound(NotInConversation);
        if (row.JoinReason != ConversationJoinReasons.Added)
            return Result.UnprocessableEntity(CannotRemoveViewer);
        if (!context.IsBase(caller.EmployeeId) && row.AddedByEmployeeId != caller.EmployeeId)
            return Result.Forbidden(NotAllowed);

        var now = DateTimeOffset.UtcNow;
        var names = await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId, command.EmployeeId], ct);
        var text = $"{names.GetValueOrDefault(caller.EmployeeId)?.Name ?? "A teammate"} removed {names.GetValueOrDefault(command.EmployeeId)?.Name ?? "a teammate"}";
        var system = ConversationSystemMessages.Create(caller.TenantId, context.Conversation.Id, text,
            JsonSerializer.Serialize(new { type = ParticipantRemovedEvent, by = caller.EmployeeId, employee = command.EmployeeId }),
            now, caller.UserId);

        row.RemovedAt = now;
        row.RemovedByEmployeeId = caller.EmployeeId;
        _conversations.UpdateParticipant(row);
        await _conversations.AddMessageAsync(system, [], ct);
        context.Conversation.LastMessageAt = now;
        _conversations.Update(context.Conversation);
        await _unitOfWork.SaveChangesAsync(ct);

        await ConversationRealtime.AfterDepartureAsync(
            _realtime, _logger, caller.TenantId, context, command.EmployeeId, system, ct);
        return Result.Success();
    }
}
