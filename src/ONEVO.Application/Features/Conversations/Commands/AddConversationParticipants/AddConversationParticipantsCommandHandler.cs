using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;

public sealed class AddConversationParticipantsCommandHandler
    : IRequestHandler<AddConversationParticipantsCommand, Result<IReadOnlyList<ConversationParticipantResponse>>>
{
    public const string ParticipantAddedEvent = "participant_added";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ConversationNotifier _notifier;
    private readonly ConversationResponseBuilder _responses;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<AddConversationParticipantsCommandHandler> _logger;

    public AddConversationParticipantsCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership,
        ConversationNotifier notifier, ConversationResponseBuilder responses, IInboxRealtimePublisher realtime,
        ILogger<AddConversationParticipantsCommandHandler> logger)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _notifier = notifier;
        _responses = responses;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ConversationParticipantResponse>>> Handle(
        AddConversationParticipantsCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ConversationParticipantResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<IReadOnlyList<ConversationParticipantResponse>>.Forbidden(ConversationRules.NoEmployeeMessage);

        var context = await _access.LoadAsync(tenantId, command.ConversationId, caller.Value, ct);
        if (context is null)
            return Result<IReadOnlyList<ConversationParticipantResponse>>.NotFound(ConversationRules.NotFoundMessage);

        var toAdd = (command.EmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct()
            .Where(id => !context.ParticipantIds.Contains(id)).ToList();
        if (toAdd.Count == 0)
            return Result<IReadOnlyList<ConversationParticipantResponse>>.Success([]);

        foreach (var employeeId in toAdd)
        {
            if (await _membership.GetActiveAssigneeAsync(tenantId, employeeId, ct) is null)
                return Result<IReadOnlyList<ConversationParticipantResponse>>.UnprocessableEntity(
                    "Only active employees of this organization can be added.");
        }

        var people = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, [caller.Value, .. toAdd], ct);
        var actorName = people.GetValueOrDefault(caller.Value)?.Name ?? "A teammate";
        var now = DateTimeOffset.UtcNow;
        var added = new List<(ConversationParticipant Row, ConversationMessage System)>();

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var employeeId in toAdd)
            {
                var row = await AddRowAsync(tenantId, context, employeeId, caller.Value, now, innerCt);
                var system = SystemMessage(tenantId, context.Conversation.Id, caller.Value, employeeId,
                    actorName, people.GetValueOrDefault(employeeId)?.Name ?? "a teammate", now);
                await _conversations.AddMessageAsync(system, [], innerCt);
                added.Add((row, system));
            }
            context.Conversation.LastMessageAt = now;
            _conversations.Update(context.Conversation);
            await _notifier.NotifyAsync(
                tenantId, ConversationNotifier.MemberAddedTemplate, toAdd, actorName, context.SubjectInfo.Title, context.Subject, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        var responses = added.Select(a => ConversationResponseBuilder.Participant(
            a.Row.EmployeeId, people.GetValueOrDefault(a.Row.EmployeeId), ConversationJoinReasons.Added, caller, now)).ToList();
        IReadOnlyCollection<Guid> recipients = context.ParticipantIds.Concat(toAdd).ToList();
        foreach (var (row, system) in added)
        {
            var participant = responses.First(r => r.EmployeeId == row.EmployeeId);
            await ConversationRules.PublishSafelyAsync(
                () => _realtime.ParticipantAddedAsync(tenantId, recipients, context.Conversation.Id, participant, ct),
                _logger, "participantAdded");
            var message = ConversationResponseBuilder.Message(system, [], author: null);
            await ConversationRules.PublishSafelyAsync(
                () => _realtime.MessageCreatedAsync(tenantId, recipients, context.Conversation.Id, message, ct),
                _logger, "messageCreated");
        }
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.InboxChangedAsync(tenantId, toAdd, context.Subject, ct), _logger, "inboxChanged");

        return Result<IReadOnlyList<ConversationParticipantResponse>>.Success(responses);
    }

    /// <summary>A former base participant (e.g. a transferred approver), or someone who was removed or left, already
    /// has a row: it is re-labelled as added (and made active again) rather than duplicated.</summary>
    private async Task<ConversationParticipant> AddRowAsync(
        Guid tenantId, ConversationContext context, Guid employeeId, Guid caller, DateTimeOffset now, CancellationToken ct)
    {
        if (context.Rows.FirstOrDefault(r => r.EmployeeId == employeeId) is { } existing)
        {
            existing.JoinReason = ConversationJoinReasons.Added;
            existing.AddedByEmployeeId = caller;
            existing.AddedAt = now;
            existing.RemovedAt = null;
            existing.RemovedByEmployeeId = null;
            existing.LeftAt = null;
            _conversations.UpdateParticipant(existing);
            return existing;
        }

        var row = new ConversationParticipant
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = context.Conversation.Id, EmployeeId = employeeId,
            JoinReason = ConversationJoinReasons.Added, AddedByEmployeeId = caller, AddedAt = now,
            CreatedById = _currentUser.UserId, CreatedAt = now
        };
        await _conversations.AddParticipantIfMissingAsync(row, ct);
        return row;
    }

    private ConversationMessage SystemMessage(
        Guid tenantId, Guid conversationId, Guid by, Guid employee, string byName, string employeeName, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, AuthorEmployeeId = null,
            Kind = ConversationMessageKinds.System,
            BodyHtml = $"{WebUtility.HtmlEncode(byName)} added {WebUtility.HtmlEncode(employeeName)}",
            SystemEventJson = JsonSerializer.Serialize(new { type = ParticipantAddedEvent, by, employee }),
            CreatedById = _currentUser.UserId, CreatedAt = now
        };
}
