using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;

public class UpdateTenantCommandHandler : IRequestHandler<UpdateTenantCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateTenantCommandHandler(
        ITenantRepository tenants,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateTenantCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure("Authentication required.", 403);

        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result.NotFound($"Tenant '{request.TenantId}' not found.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                return Result.Failure("name must not be empty when provided.");
            tenant.Name = name;
        }

        if (request.Slug is not null)
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (await _tenants.SlugExistsAsync(slug, excludeId: tenant.Id, ct))
                return Result.Failure($"slug '{slug}' is already taken.", 409);
            tenant.Slug = slug;
        }

        if (request.IndustryProfile is not null)
            tenant.IndustryProfile = request.IndustryProfile.Trim();

        tenant.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
