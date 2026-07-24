using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Users.DTOs;
using ONEVO.Application.Features.Users.RepositoryInterfaces;

namespace ONEVO.Application.Features.Users.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler
    : IRequestHandler<GetMyProfileQuery, Result<MyProfileDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserProfileRepository _repo;

    public GetMyProfileQueryHandler(ICurrentUser currentUser, IUserProfileRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<Result<MyProfileDto>> Handle(GetMyProfileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyProfileDto>.Failure("Authentication required.", 401);

        var userId = _currentUser.UserId;

        var employee = await _repo.GetEmployeeByUserIdAsync(userId, ct);

        var devices = employee is not null
            ? await _repo.GetAgentsByEmployeeIdAsync(employee.Id, ct)
            : (IReadOnlyList<ONEVO.Domain.Features.AgentGateway.Entities.RegisteredAgent>)Array.Empty<ONEVO.Domain.Features.AgentGateway.Entities.RegisteredAgent>();

        var workLocation = employee is not null
            ? await _repo.GetWorkLocationSettingsAsync(employee.Id, ct)
            : null;

        var faceScan = employee is not null
            ? await _repo.GetActiveReferencePhotoAsync(employee.Id, ct)
            : null;

        var dto = new MyProfileDto(
            UserId: userId,
            TenantId: _currentUser.TenantId,
            Email: _currentUser.Email,
            Employee: employee is null ? null : new EmployeeProfileDto(
                Id: employee.Id,
                EmployeeNumber: employee.EmployeeNumber,
                FirstName: employee.FirstName,
                LastName: employee.LastName,
                Email: employee.Email,
                Phone: employee.Phone,
                HireDate: employee.HireDate,
                EmploymentStatusId: employee.EmploymentStatusId,
                WorkModeId: employee.WorkModeId
            ),
            Devices: devices.Select(a => new MyDeviceDto(
                AgentId: a.Id,
                DeviceName: a.DeviceName,
                OsVersion: a.OsVersion,
                AgentVersion: a.AgentVersion,
                Status: a.Status,
                LastHeartbeatAt: a.LastHeartbeatAt
            )).ToList(),
            WorkLocation: workLocation is null ? null : new WorkLocationDto(
                WorkMode: workLocation.WorkMode,
                VerificationEnabled: workLocation.WorkLocationVerificationEnabled,
                GracePeriodMinutes: workLocation.GracePeriodMinutes,
                PhotoChallengeOnMismatch: workLocation.PhotoChallengeOnMismatch
            ),
            FaceScan: faceScan is null ? null : new FaceScanDto(
                Status: faceScan.Status,
                Source: faceScan.Source,
                CapturedAt: faceScan.CapturedAt,
                IsActive: faceScan.IsActive
            )
        );

        return Result<MyProfileDto>.Success(dto);
    }
}
