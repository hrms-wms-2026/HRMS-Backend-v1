using System.Text.Json.Serialization;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetSelfEmployeeSetupStatus;

/// <summary>Whether the signed-in user can "Add yourself as an employee": they have no employee profile yet
/// and the company has what the Add Employee wizard needs. The legal entity, department and position flags
/// also drive the owner's step-by-step setup guide. Also returns their login name and email for the
/// wizard prefill. Seat capacity is still enforced by finalize.</summary>
public record GetSelfEmployeeSetupStatusQuery : IRequest<Result<SelfEmployeeSetupStatusResponse>>;

public sealed record SelfEmployeeSetupStatusResponse(
    [property: JsonPropertyName("has_employee_profile")] bool HasEmployeeProfile,
    [property: JsonPropertyName("has_legal_entity")] bool HasLegalEntity,
    [property: JsonPropertyName("has_department")] bool HasDepartment,
    [property: JsonPropertyName("has_position")] bool HasPosition,
    [property: JsonPropertyName("can_add_self")] bool CanAddSelf,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("work_email")] string WorkEmail);

public sealed class GetSelfEmployeeSetupStatusQueryHandler
    : IRequestHandler<GetSelfEmployeeSetupStatusQuery, Result<SelfEmployeeSetupStatusResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IUserRepository _users;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IDepartmentRepository _departments;
    private readonly IPositionRepository _positions;

    public GetSelfEmployeeSetupStatusQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        IUserRepository users,
        ILegalEntityRepository legalEntities,
        IDepartmentRepository departments,
        IPositionRepository positions)
    {
        _currentUser = currentUser;
        _employees = employees;
        _users = users;
        _legalEntities = legalEntities;
        _departments = departments;
        _positions = positions;
    }

    public async Task<Result<SelfEmployeeSetupStatusResponse>> Handle(
        GetSelfEmployeeSetupStatusQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var hasProfile = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct) is not null;
        var hasLegalEntity = await _legalEntities.CountActiveByTenantAsync(tenantId, ct) > 0;
        var hasDepartment = await _departments.AnyActiveAsync(tenantId, ct);
        var hasPosition = await _positions.AnyActiveWithDepartmentAsync(tenantId, ct);
        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);

        return Result<SelfEmployeeSetupStatusResponse>.Success(new SelfEmployeeSetupStatusResponse(
            hasProfile, hasLegalEntity, hasDepartment, hasPosition,
            CanAddSelf: !hasProfile && hasLegalEntity && hasPosition,
            user?.FirstName ?? string.Empty, user?.LastName ?? string.Empty, user?.Email ?? string.Empty));
    }
}
