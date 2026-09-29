using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;

public sealed record RemoveTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>;
