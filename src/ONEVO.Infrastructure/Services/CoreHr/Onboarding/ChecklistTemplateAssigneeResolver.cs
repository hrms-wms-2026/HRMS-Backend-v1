using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.CoreHr.Onboarding;

public sealed class ChecklistTemplateAssigneeResolver(
    IPositionAssignmentRepository positionAssignmentRepository,
    IEmployeeRepository employeeRepository) : IChecklistTemplateAssigneeResolver
{
    public async Task<Result<Guid>> ResolveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)
    {
        var previews = await positionAssignmentRepository.GetOccupancyPreviewsAsync(tenantId, [positionId], previewLimit: 1, ct);

        if (!previews.TryGetValue(positionId, out var preview) || preview.AssignedCount == 0)
            return Result<Guid>.UnprocessableEntity(
                "The selected assignee position has no active occupant; provide a concrete assignedToId instead.");

        if (preview.AssignedCount > 1)
            return Result<Guid>.UnprocessableEntity(
                "The selected assignee position has multiple active occupants; specify the concrete user/employee (assignedToId) instead.");

        var occupantEmployeeId = preview.OccupantPreview.Single().EmployeeId;
        var employee = await employeeRepository.GetByIdAsync(tenantId, occupantEmployeeId, ct);
        if (employee is null)
            return Result<Guid>.UnprocessableEntity("The selected assignee position's occupant could not be resolved to a user.");

        return Result<Guid>.Success(employee.UserId);
    }
}
