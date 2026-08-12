using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

public class ListEmployeesQueryHandler : IRequestHandler<ListEmployeesQuery, Result<EmployeeListPageResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly ICurrentUser _currentUser;

    public ListEmployeesQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeListPageResponse>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct);

        var (items, totalCount) = await _employeeRepository.ListVisibleAsync(
            _currentUser.TenantId,
            scope,
            new EmployeeListFilter(request.Search, request.DepartmentId, request.LegalEntityId),
            page,
            pageSize,
            ct);

        return Result<EmployeeListPageResponse>.Success(
            new EmployeeListPageResponse(items, totalCount, page, pageSize));
    }
}
