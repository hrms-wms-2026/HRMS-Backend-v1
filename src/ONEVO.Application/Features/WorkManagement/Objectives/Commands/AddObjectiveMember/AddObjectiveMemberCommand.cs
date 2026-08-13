using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public sealed record AddObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result>;
