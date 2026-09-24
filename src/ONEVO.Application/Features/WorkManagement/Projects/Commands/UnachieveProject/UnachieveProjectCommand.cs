using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;

public sealed record UnachieveProjectCommand(Guid ProjectId) : IRequest<Result>;
