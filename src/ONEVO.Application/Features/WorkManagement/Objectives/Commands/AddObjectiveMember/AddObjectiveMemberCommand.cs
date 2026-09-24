using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public sealed record AddObjectiveMemberCommand(Guid ObjectiveId, Guid EmployeeId) : IRequest<Result<AddObjectiveMemberOutcomeResponse>>;
