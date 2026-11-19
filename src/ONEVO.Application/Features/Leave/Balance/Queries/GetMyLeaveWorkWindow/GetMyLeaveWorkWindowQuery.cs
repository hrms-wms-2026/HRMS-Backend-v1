using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetMyLeaveWorkWindow;

public sealed record GetMyLeaveWorkWindowQuery : IRequest<Result<LeaveWorkWindowResponse>>;
