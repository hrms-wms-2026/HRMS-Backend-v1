using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetSnapshots;

public record GetSnapshotsQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<ActivitySnapshotDto>>>;
