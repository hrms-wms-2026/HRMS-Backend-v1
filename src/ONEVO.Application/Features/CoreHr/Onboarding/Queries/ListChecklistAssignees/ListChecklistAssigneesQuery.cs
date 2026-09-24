using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssignees;

public record ListChecklistAssigneesQuery(Guid LegalEntityId, Guid PositionId)
    : IRequest<Result<IReadOnlyList<ChecklistAssignee>>>;
