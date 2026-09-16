using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.ListWorkModes;

public record ListWorkModesQuery(Guid LegalEntityId, bool IncludeInactive)
    : IRequest<Result<IReadOnlyList<WorkModeResponse>>>;
