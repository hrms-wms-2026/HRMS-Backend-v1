using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public class GetOffboardingQueryHandler(IOffboardingRecordRepository repository, ICurrentUser currentUser)
    : IRequestHandler<GetOffboardingQuery, Result<OffboardingRecordResponse?>>
{
    public async Task<Result<OffboardingRecordResponse?>> Handle(GetOffboardingQuery request, CancellationToken ct)
    {
        var record = await repository.GetLatestByEmployeeIdAsync(currentUser.TenantId, request.EmployeeId, ct);
        if (record is null)
            return Result<OffboardingRecordResponse?>.Success(null);

        return Result<OffboardingRecordResponse?>.Success(new OffboardingRecordResponse(
            record.Id, record.EmployeeId, record.Reason, record.LastWorkingDate, record.KnowledgeRiskLevel,
            record.RehireEligibility, record.Notes, record.ChecklistTemplateId, record.Status,
            record.CreatedAt, record.UpdatedAt, record.CompletedAt));
    }
}
