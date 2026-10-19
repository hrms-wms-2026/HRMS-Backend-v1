using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.MoveReportingLines;

public sealed record MoveReportingLinesCommand(
    Guid LegalEntityId,
    IReadOnlyCollection<Guid> SelectedEmployeeIds,
    Guid NewManagerEmployeeId,
    string? Reason) : IRequest<Result<ReportingLineMoveImpactResponse>>;

public sealed class MoveReportingLinesCommandHandler(
    IReportingLineMoveService service,
    ICurrentUser currentUser)
    : IRequestHandler<MoveReportingLinesCommand, Result<ReportingLineMoveImpactResponse>>
{
    public Task<Result<ReportingLineMoveImpactResponse>> Handle(
        MoveReportingLinesCommand request,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Task.FromResult(Result<ReportingLineMoveImpactResponse>.Forbidden("Authentication required."));

        return service.MoveAsync(
            currentUser.TenantId,
            request.LegalEntityId,
            request.SelectedEmployeeIds,
            request.NewManagerEmployeeId,
            request.Reason,
            ct);
    }
}
