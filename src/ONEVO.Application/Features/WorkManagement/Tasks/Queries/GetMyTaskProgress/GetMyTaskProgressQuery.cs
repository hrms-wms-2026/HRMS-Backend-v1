using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskProgress;

/// <summary>Completed/In Progress/Not Started/Overdue breakdown across every task assigned to
/// the current employee, for the Task Progress dashboard donut widget. With <paramref name="From"/>
/// and/or <paramref name="To"/> (inclusive) only tasks due in that range count - the home
/// dashboard's This week / Last week / This month / Last month picker; tasks without a due date are
/// then left out.</summary>
public sealed record GetMyTaskProgressQuery(DateOnly? From = null, DateOnly? To = null) : IRequest<Result<TaskProgressResponse>>;
