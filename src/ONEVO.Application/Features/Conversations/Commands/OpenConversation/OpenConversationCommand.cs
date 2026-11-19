using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Commands.OpenConversation;

/// <summary>Gets (or creates) the conversation of an inbox item and returns it with its newest page of messages.</summary>
public sealed record OpenConversationCommand(string Source, string Kind, Guid Id) : IRequest<Result<ConversationResponse>>;
