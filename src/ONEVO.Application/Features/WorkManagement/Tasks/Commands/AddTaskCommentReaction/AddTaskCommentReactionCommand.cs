using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;

public sealed record AddTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>;
