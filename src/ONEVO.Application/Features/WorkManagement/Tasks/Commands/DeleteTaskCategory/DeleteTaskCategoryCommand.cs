using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskCategory;

public sealed record DeleteTaskCategoryCommand(Guid CategoryId) : IRequest<Result>;
