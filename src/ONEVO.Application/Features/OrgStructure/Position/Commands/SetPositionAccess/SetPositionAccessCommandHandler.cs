using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetPositionAccess;

public class SetPositionAccessCommandHandler : IRequestHandler<SetPositionAccessCommand, Result<PositionAccessTemplateResponse>>
{
    private readonly IPositionRepository _repository;
    private readonly IRoleRepository _roleRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public SetPositionAccessCommandHandler(
        IPositionRepository repository,
        IRoleRepository roleRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _roleRepository = roleRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<PositionAccessTemplateResponse>> Handle(SetPositionAccessCommand request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var position = await _repository.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (position is null)
            return Result<PositionAccessTemplateResponse>.Failure("Position not found or does not belong to this Company.", 404);

        var role = await _roleRepository.GetByIdForTenantAsync(tenantId, request.RoleId, ct);
        if (role is null)
            return Result<PositionAccessTemplateResponse>.Failure("Selected System Access Role does not exist.", 422);

        var now = _clock.UtcNow;
        var template = await _repository.GetAccessTemplateByPositionAsync(tenantId, request.PositionId, ct);

        if (template is null)
        {
            template = new PositionAccessTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PositionId = request.PositionId,
                RoleId = request.RoleId,
                RequiresApproval = request.RequiresApproval,
                IsActive = true,
                CreatedAt = now
            };
            await _repository.AddAccessTemplateAsync(template, ct);
        }
        else
        {
            template.RoleId = request.RoleId;
            template.RequiresApproval = request.RequiresApproval;
            template.IsActive = true;
            template.UpdatedAt = now;
            _repository.UpdateAccessTemplate(template);
        }

        await _repository.SaveChangesAsync(ct);

        return Result<PositionAccessTemplateResponse>.Success(new PositionAccessTemplateResponse(
            template.PositionId, template.RoleId, role.Name, template.RequiresApproval, template.IsActive));
    }
}
