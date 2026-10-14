using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;

/// <summary><see cref="AttachmentFileIds"/>: files the caller uploaded (purpose conversation_attachment), at most 5,
/// each up to 25 MB. <see cref="Tag"/>: null, or "justification" (only the employee the item is about).</summary>
public sealed record PostConversationMessageCommand(
    Guid ConversationId, string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds,
    IReadOnlyList<Guid>? AttachmentFileIds = null, string? Tag = null)
    : IRequest<Result<ConversationMessageResponse>>;
