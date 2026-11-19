using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;

public sealed record GetEmployeeNeedsAttentionQuery(Guid EmployeeId) : IRequest<Result<EmployeeNeedsAttentionResponse>>;
