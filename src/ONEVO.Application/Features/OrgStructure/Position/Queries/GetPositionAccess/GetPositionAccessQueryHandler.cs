using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionAccess;

public class GetPositionAccessQueryHandler : IRequestHandler<GetPositionAccessQuery, Result<PositionAccessTemplateResponse>>
{
    private readonly IPositionRepository _repository;
    private readonly IRoleRepository _roleRepository;
    private readonly ITenantContext _tenantContext;

    public GetPositionAccessQueryHandler(
        IPositionRepository repository,
        IRoleRepository roleRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _roleRepository = roleRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PositionAccessTemplateResponse>> Handle(GetPositionAccessQuery request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var position = await _repository.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (position is null)
            return Result<PositionAccessTemplateResponse>.Failure("Position not found or does not belong to this Company.", 404);

        var template = await _repository.GetAccessTemplateByPositionIncludingInactiveAsync(tenantId, request.PositionId, ct);
        if (template is null)
        {
            return Result<PositionAccessTemplateResponse>.Success(new PositionAccessTemplateResponse(
                request.PositionId, null, null, false, false));
        }

        var role = await _roleRepository.GetByIdForTenantAsync(tenantId, template.RoleId, ct);

        return Result<PositionAccessTemplateResponse>.Success(new PositionAccessTemplateResponse(
            template.PositionId, template.RoleId, role?.Name, template.RequiresApproval, template.IsActive));
    }
}
