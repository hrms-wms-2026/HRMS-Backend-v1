using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;

public sealed record DeleteTaskCommentCommand(Guid CommentId) : IRequest<Result>;
