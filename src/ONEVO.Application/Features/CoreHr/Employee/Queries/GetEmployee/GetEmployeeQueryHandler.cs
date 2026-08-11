using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;

public class GetEmployeeQueryHandler : IRequestHandler<GetEmployeeQuery, Result<EmployeeListItemResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly ICurrentUser _currentUser;

    public GetEmployeeQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeListItemResponse>> Handle(GetEmployeeQuery request, CancellationToken ct)
    {
        var existing = await _employeeRepository.GetByIdAsync(_currentUser.TenantId, request.EmployeeId, ct);
        if (existing is null)
        {
            return Result<EmployeeListItemResponse>.NotFound(
                "The employee or selected organization record could not be found.");
        }

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct);

        var visible = await _employeeRepository.GetVisibleByIdAsync(
            _currentUser.TenantId, scope, request.EmployeeId, ct);

        if (visible is null)
        {
            return Result<EmployeeListItemResponse>.Forbidden(
                "You do not have access to manage this employee.");
        }

        return Result<EmployeeListItemResponse>.Success(visible);
    }
}
