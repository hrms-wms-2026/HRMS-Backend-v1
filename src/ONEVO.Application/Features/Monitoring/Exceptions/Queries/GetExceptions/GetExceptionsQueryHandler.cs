using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;

public class GetExceptionsQueryHandler : IRequestHandler<GetExceptionsQuery, Result<PagedResult<ExceptionDto>>>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ICurrentUser _currentUser;
    private readonly IExceptionScopeResolver _scope;
    private readonly IEmployeeRepository _employees;

    public GetExceptionsQueryHandler(
        IExceptionRepository exceptions, ICurrentUser currentUser,
        IExceptionScopeResolver scope, IEmployeeRepository employees)
    {
        _exceptions = exceptions;
        _currentUser = currentUser;
        _scope = scope;
        _employees = employees;
    }

    public async Task<Result<PagedResult<ExceptionDto>>> Handle(GetExceptionsQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        IReadOnlyCollection<Guid> candidates = tenantId == Guid.Empty
            ? []
            : await _exceptions.ListEmployeeIdsWithExceptionsAsync(tenantId, ct);
        var scope = await _scope.ResolveAsync(forAction: false, candidates, ct);
        if (scope is null)
            return Result<PagedResult<ExceptionDto>>.Forbidden();

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        var filter = new ExceptionListFilter(
            request.Status, request.Type,
            scope.IsHr ? null : scope.EmployeeIds,
            scope.ActorEmployeeId);

        var total = await _exceptions.GetListTotalCountAsync(tenantId, filter, ct);
        var items = await _exceptions.GetListAsync(tenantId, filter, page, pageSize, ct);

        var employeeIds = items.Select(e => e.EmployeeId).Distinct().ToList();
        var employees = employeeIds.Count == 0
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : await _employees.ListByIdsAsync(tenantId, employeeIds, ct);

        var dtos = items.Select(e => new ExceptionDto(
            e.Id, e.EmployeeId, e.Type.ToString(), e.Status.ToString(), e.Title, e.Description,
            e.DetectedAt, e.AcknowledgedAt, e.ResolvedAt, e.EscalatedAt,
            employees.TryGetValue(e.EmployeeId, out var employee)
                ? $"{employee.FirstName} {employee.LastName}".Trim()
                : string.Empty,
            e.ResolutionNote)).ToList();

        return Result<PagedResult<ExceptionDto>>.Success(new PagedResult<ExceptionDto>(dtos, page, pageSize, total));
    }
}
