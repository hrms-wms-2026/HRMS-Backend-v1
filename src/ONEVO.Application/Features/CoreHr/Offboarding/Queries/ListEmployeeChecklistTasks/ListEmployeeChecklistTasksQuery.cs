using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public sealed record ListEmployeeChecklistTasksQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EmployeeChecklistTaskResponse>>>;
