using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.PreviewReportingLineMove;

public sealed record PreviewReportingLineMoveCommand(
    Guid LegalEntityId,
    IReadOnlyCollection<Guid> SelectedEmployeeIds,
    Guid NewManagerEmployeeId) : IRequest<Result<ReportingLineMoveImpactResponse>>;

public sealed class PreviewReportingLineMoveCommandHandler(
    IReportingLineMoveService service,
    ICurrentUser currentUser)
    : IRequestHandler<PreviewReportingLineMoveCommand, Result<ReportingLineMoveImpactResponse>>
{
    public Task<Result<ReportingLineMoveImpactResponse>> Handle(
        PreviewReportingLineMoveCommand request,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Task.FromResult(Result<ReportingLineMoveImpactResponse>.Forbidden("Authentication required."));

        return service.PreviewAsync(
            currentUser.TenantId,
            request.LegalEntityId,
            request.SelectedEmployeeIds,
            request.NewManagerEmployeeId,
            ct);
    }
}
