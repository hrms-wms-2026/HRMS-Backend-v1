using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public class EditSprintCommandHandler : IRequestHandler<EditSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public EditSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ISprintRepository sprints, ISprintAccessService access, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _access = access;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(EditSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        if (!await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only the sprint's creator or an owner of one of its tasks' modules can edit this sprint.");

        if (sprint.Status is SprintStatuses.Complete or SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already ended and can no longer be edited.");

        if (sprint.Status == SprintStatuses.Draft && (request.StartDate is not null || request.EndDate is not null))
            return Result<SprintResponse>.Failure("A Draft sprint has no dates yet - start it to set dates.", 422);

        if (sprint.Status == SprintStatuses.Active && request.StartDate is not null && request.EndDate is not null
            && request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Name = request.Name.Trim();
            sprint.Goal = request.Goal?.Trim();
            if (sprint.Status == SprintStatuses.Active && request.StartDate is not null && request.EndDate is not null)
            {
                sprint.StartDate = request.StartDate;
                sprint.EndDate = request.EndDate;
            }
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Edited,
                details: new { name = sprint.Name, goal = sprint.Goal, startDate = sprint.StartDate, endDate = sprint.EndDate }), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
