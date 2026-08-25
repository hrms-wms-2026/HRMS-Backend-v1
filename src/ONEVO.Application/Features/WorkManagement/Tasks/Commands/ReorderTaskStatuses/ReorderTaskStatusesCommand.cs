using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public sealed record TaskStatusOrderUpdate(Guid StatusId, int DisplayOrder, string Visibility, bool MarksTaskComplete);

public sealed record ReorderTaskStatusesCommand(Guid ProjectId, List<TaskStatusOrderUpdate> Updates) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
