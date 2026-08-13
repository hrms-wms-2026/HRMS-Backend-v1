using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ChecklistTemplateEntity = ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;

public class CreateChecklistTemplateCommandHandler(
    IChecklistTemplateRepository templateRepository,
    ILegalEntityRepository legalEntityRepository,
    IDepartmentRepository departmentRepository,
    IPositionRepository positionRepository,
    ChecklistTemplateTaskInputResolver taskInputResolver,
    ICurrentUser currentUser)
    : IRequestHandler<CreateChecklistTemplateCommand, Result<ChecklistTemplateResponse>>
{
    public async Task<Result<ChecklistTemplateResponse>> Handle(CreateChecklistTemplateCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var legalEntity = await legalEntityRepository.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null || !legalEntity.IsActive)
            return Result<ChecklistTemplateResponse>.UnprocessableEntity("The selected legal entity does not exist or is inactive.");

        var scopeError = await ChecklistTemplateScopeValidator.ValidateAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, request.PositionId, departmentRepository, positionRepository, ct);
        if (scopeError is not null)
            return Result<ChecklistTemplateResponse>.UnprocessableEntity(scopeError);

        var inputs = request.Tasks
            .Select(t => new ChecklistTemplateTaskInput(t.Title, t.OwnerType, t.AssignedToId, t.AssigneePositionId, t.DueOffsetDays, t.Sequence, t.IsRequired))
            .ToList();
        var resolved = await taskInputResolver.ResolveAsync(tenantId, inputs, ct);
        if (!resolved.IsSuccess)
            return Result<ChecklistTemplateResponse>.Failure(resolved.Error!, resolved.StatusCode ?? 422);

        var template = new ChecklistTemplateEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            TemplateType = request.TemplateType,
            LegalEntityId = request.LegalEntityId,
            DepartmentId = request.DepartmentId,
            PositionId = request.PositionId,
            TasksJson = ChecklistTaskJsonContract.SerializeTemplateTasks(resolved.Value!),
            IsActive = true,
        };
        await templateRepository.AddAsync(template, ct);
        await templateRepository.SaveChangesAsync(ct);

        return Result<ChecklistTemplateResponse>.Success(ChecklistTemplateResponseMapper.ToResponse(template, resolved.Value!));
    }
}
