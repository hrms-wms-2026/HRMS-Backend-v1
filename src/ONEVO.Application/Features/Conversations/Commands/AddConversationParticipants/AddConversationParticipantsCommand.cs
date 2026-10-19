using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;

/// <summary>Any participant may add employees; returns the participants that were newly added.</summary>
public sealed record AddConversationParticipantsCommand(Guid ConversationId, IReadOnlyList<Guid> EmployeeIds)
    : IRequest<Result<IReadOnlyList<ConversationParticipantResponse>>>;
