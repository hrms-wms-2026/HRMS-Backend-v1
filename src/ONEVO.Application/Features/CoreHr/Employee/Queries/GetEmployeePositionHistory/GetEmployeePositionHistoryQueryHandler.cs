using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeePositionHistory;

public class GetEmployeePositionHistoryQueryHandler
    : IRequestHandler<GetEmployeePositionHistoryQuery, Result<IReadOnlyList<PositionHistoryEntryResponse>>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly ICurrentUser _currentUser;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IAccessGrantRequestRepository _accessGrantRequestRepository;
    private readonly IUserRepository _userRepository;

    public GetEmployeePositionHistoryQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        ICurrentUser currentUser,
        IPositionAssignmentRepository positionAssignmentRepository,
        IPositionRepository positionRepository,
        IAccessGrantRequestRepository accessGrantRequestRepository,
        IUserRepository userRepository)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _currentUser = currentUser;
        _positionAssignmentRepository = positionAssignmentRepository;
        _positionRepository = positionRepository;
        _accessGrantRequestRepository = accessGrantRequestRepository;
        _userRepository = userRepository;
    }

    public async Task<Result<IReadOnlyList<PositionHistoryEntryResponse>>> Handle(
        GetEmployeePositionHistoryQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var existing = await _employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (existing is null)
            return Result<IReadOnlyList<PositionHistoryEntryResponse>>.NotFound("The employee or selected organization record could not be found.");

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);

        var visible = await _employeeRepository.GetVisibleByIdAsync(tenantId, scope, request.EmployeeId, ct);
        if (visible is null)
            return Result<IReadOnlyList<PositionHistoryEntryResponse>>.Forbidden("You do not have access to manage this employee.");

        var history = await _positionAssignmentRepository.ListHistoryForEmployeeAsync(tenantId, request.EmployeeId, ct);
        var positionIds = history.Select(h => h.PositionId).Distinct().ToList();
        var positions = await _positionRepository.GetByIdsAsync(tenantId, positionIds, ct);
        var positionsById = positions.ToDictionary(p => p.Id);

        var userIds = history.Select(h => h.CreatedById).Distinct().ToList();
        var approvedByUserIds = await _accessGrantRequestRepository.GetApprovedByUserIdsForAssignmentsAsync(
            tenantId, history.Select(h => h.Id).ToList(), ct);
        userIds.AddRange(approvedByUserIds.Values);
        var users = await _userRepository.GetByIdsAsync(userIds.Distinct().ToList(), ct);
        var usersById = users.ToDictionary(u => u.Id);

        var entries = history.Select(h =>
        {
            var position = positionsById.GetValueOrDefault(h.PositionId);
            var approvedByUserId = approvedByUserIds.GetValueOrDefault(h.Id);
            return new PositionHistoryEntryResponse(
                position?.Name ?? "Unknown position",
                null,
                h.EffectiveFrom, h.EffectiveTo, h.ChangeReason,
                FormatUserName(usersById.GetValueOrDefault(h.CreatedById)),
                approvedByUserId != Guid.Empty ? FormatApprovedByName(usersById.GetValueOrDefault(approvedByUserId)) : null);
        }).ToList();

        return Result<IReadOnlyList<PositionHistoryEntryResponse>>.Success(entries);
    }

    private static string FormatUserName(User? user)
    {
        if (user is null)
            return "Unknown";
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? "Unknown" : name;
    }

    private static string? FormatApprovedByName(User? user)
    {
        if (user is null)
            return null;
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
