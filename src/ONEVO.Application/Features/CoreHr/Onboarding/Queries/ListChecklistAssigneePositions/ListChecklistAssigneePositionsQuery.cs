using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssigneePositions;

public sealed record ChecklistAssigneePosition(Guid Id, string Name);

public record ListChecklistAssigneePositionsQuery(Guid LegalEntityId)
    : IRequest<Result<IReadOnlyList<ChecklistAssigneePosition>>>;
