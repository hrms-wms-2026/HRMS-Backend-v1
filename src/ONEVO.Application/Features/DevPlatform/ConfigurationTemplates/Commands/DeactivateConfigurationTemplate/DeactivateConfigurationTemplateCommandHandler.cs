using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.DeactivateConfigurationTemplate;

public sealed class DeactivateConfigurationTemplateCommandHandler
    : IRequestHandler<DeactivateConfigurationTemplateCommand, Result<ConfigurationTemplateDto>>
{
    private readonly IConfigurationTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;

    public DeactivateConfigurationTemplateCommandHandler(
        IConfigurationTemplateRepository templates,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ConfigurationTemplateDto>> Handle(
        DeactivateConfigurationTemplateCommand request,
        CancellationToken ct)
    {
        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
        {
            return Result<ConfigurationTemplateDto>.NotFound("Configuration template not found.");
        }

        // NOTE: the documented "blocked if active tenant positions/assignment rows
        // reference the template" guard is not implemented here — this foundation
        // step does not write to positions/assignment tables, so there is nothing
        // to check yet. Deactivation is unconditional. See plan Global Constraints.
        template.IsActive = false;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ConfigurationTemplateDto>.Success(ConfigurationTemplateMapper.ToDto(template));
    }
}
