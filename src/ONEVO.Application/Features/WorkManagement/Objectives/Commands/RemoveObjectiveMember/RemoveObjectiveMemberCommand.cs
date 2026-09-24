using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public sealed record RemoveObjectiveMemberCommand(Guid ObjectiveId, Guid EmployeeId) : IRequest<Result>;
