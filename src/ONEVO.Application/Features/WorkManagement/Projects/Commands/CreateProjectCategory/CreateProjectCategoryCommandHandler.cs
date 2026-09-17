using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProjectCategory;

public class CreateProjectCategoryCommandHandler : IRequestHandler<CreateProjectCategoryCommand, Result<ProjectCategoryListItemResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectCategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProjectCategoryCommandHandler(
        ICurrentUser currentUser,
        IProjectCategoryRepository categories,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectCategoryListItemResponse>> Handle(CreateProjectCategoryCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectCategoryListItemResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ProjectCategoryListItemResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        if (await _categories.ExistsByNameAsync(tenantId, name, ct))
            return Result<ProjectCategoryListItemResponse>.Conflict("A project category with this name already exists.");

        var category = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsActive = true,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _categories.AddAsync(category, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProjectCategoryListItemResponse>.Success(ProjectMapper.ToListItem(category));
    }
}
