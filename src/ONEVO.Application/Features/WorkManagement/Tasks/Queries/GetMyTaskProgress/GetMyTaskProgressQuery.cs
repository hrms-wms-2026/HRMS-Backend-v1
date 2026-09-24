using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskProgress;

/// <summary>Completed/In Progress/Not Started/Overdue breakdown across every task assigned to
/// the current employee, for the Task Progress dashboard donut widget.</summary>
public sealed record GetMyTaskProgressQuery : IRequest<Result<TaskProgressResponse>>;
