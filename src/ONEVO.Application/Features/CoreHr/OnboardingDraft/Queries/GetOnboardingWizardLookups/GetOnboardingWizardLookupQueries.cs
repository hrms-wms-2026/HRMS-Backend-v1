using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingWizardLookups;

public record GetOnboardingReportingChainQuery(Guid ManagerEmployeeId)
    : IRequest<Result<IReadOnlyList<ReportingChainNodeResponse>>>;

public record GetOnboardingClockInDefaultsQuery(
    Guid LegalEntityId, Guid WorkModeId, Guid? PositionId, Guid? DepartmentId)
    : IRequest<Result<OnboardingClockInDefaultsResponse>>;

public record SearchManagerCandidatesQuery(Guid LegalEntityId, string Search)
    : IRequest<Result<IReadOnlyList<ManagerCandidateResponse>>>;

public class SearchManagerCandidatesQueryHandler
    : IRequestHandler<SearchManagerCandidatesQuery, Result<IReadOnlyList<ManagerCandidateResponse>>>
{
    private readonly IOnboardingWizardLookupService _lookups;
    private readonly ICurrentUser _currentUser;

    public SearchManagerCandidatesQueryHandler(IOnboardingWizardLookupService lookups, ICurrentUser currentUser)
    {
        _lookups = lookups;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ManagerCandidateResponse>>> Handle(
        SearchManagerCandidatesQuery request, CancellationToken ct)
    {
        var results = await _lookups.SearchManagerCandidatesAsync(
            _currentUser.TenantId, request.LegalEntityId, request.Search, limit: 20, ct);
        return Result<IReadOnlyList<ManagerCandidateResponse>>.Success(results);
    }
}

public class GetOnboardingReportingChainQueryHandler
    : IRequestHandler<GetOnboardingReportingChainQuery, Result<IReadOnlyList<ReportingChainNodeResponse>>>
{
    private readonly IOnboardingWizardLookupService _lookups;
    private readonly ICurrentUser _currentUser;

    public GetOnboardingReportingChainQueryHandler(IOnboardingWizardLookupService lookups, ICurrentUser currentUser)
    {
        _lookups = lookups;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ReportingChainNodeResponse>>> Handle(
        GetOnboardingReportingChainQuery request, CancellationToken ct)
    {
        var chain = await _lookups.GetReportingChainAsync(_currentUser.TenantId, request.ManagerEmployeeId, ct);
        return Result<IReadOnlyList<ReportingChainNodeResponse>>.Success(chain);
    }
}

public class GetOnboardingClockInDefaultsQueryHandler
    : IRequestHandler<GetOnboardingClockInDefaultsQuery, Result<OnboardingClockInDefaultsResponse>>
{
    private readonly IOnboardingWizardLookupService _lookups;
    private readonly ICurrentUser _currentUser;

    public GetOnboardingClockInDefaultsQueryHandler(IOnboardingWizardLookupService lookups, ICurrentUser currentUser)
    {
        _lookups = lookups;
        _currentUser = currentUser;
    }

    public async Task<Result<OnboardingClockInDefaultsResponse>> Handle(
        GetOnboardingClockInDefaultsQuery request, CancellationToken ct)
    {
        var defaults = await _lookups.GetClockInDefaultsAsync(
            _currentUser.TenantId, request.LegalEntityId, request.WorkModeId,
            request.PositionId, request.DepartmentId, ct);
        return Result<OnboardingClockInDefaultsResponse>.Success(defaults);
    }
}
