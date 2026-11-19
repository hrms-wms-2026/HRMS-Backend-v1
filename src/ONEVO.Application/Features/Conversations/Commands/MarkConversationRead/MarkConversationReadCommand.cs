using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Conversations.Commands.MarkConversationRead;

public sealed record MarkConversationReadCommand(Guid ConversationId) : IRequest<Result>;
