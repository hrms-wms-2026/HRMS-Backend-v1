using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;

public sealed record GetMyDeadlinesQuery(DateOnly From, DateOnly To) : IRequest<Result<MyDeadlinesResponse>>;
