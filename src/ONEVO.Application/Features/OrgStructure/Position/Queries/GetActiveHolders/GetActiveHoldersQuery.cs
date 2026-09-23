using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetActiveHolders;

public record GetActiveHoldersQuery(Guid LegalEntityId, Guid PositionId)
    : IRequest<Result<IReadOnlyList<PositionActiveHolder>>>;
