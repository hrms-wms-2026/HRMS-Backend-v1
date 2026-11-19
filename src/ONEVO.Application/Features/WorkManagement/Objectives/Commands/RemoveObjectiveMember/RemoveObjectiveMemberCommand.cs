using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public sealed record RemoveObjectiveMemberCommand(Guid ObjectiveId, Guid EmployeeId) : IRequest<Result<RemoveObjectiveMemberOutcomeResponse>>;
