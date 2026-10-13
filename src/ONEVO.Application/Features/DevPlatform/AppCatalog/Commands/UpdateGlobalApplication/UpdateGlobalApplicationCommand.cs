using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.AppCatalog.DTOs;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.UpdateGlobalApplication;

public sealed record UpdateGlobalApplicationCommand(
    Guid Id,
    string AppName,
    string? Category,
    string? Publisher,
    bool IsPublic,
    bool IsProductiveDefault,
    Guid ActorId) : IRequest<Result<GlobalApplicationDto>>;

public sealed class UpdateGlobalApplicationCommandHandler(
    IGlobalApplicationRepository applications,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateGlobalApplicationCommand, Result<GlobalApplicationDto>>
{
    public async Task<Result<GlobalApplicationDto>> Handle(UpdateGlobalApplicationCommand request, CancellationToken ct)
    {
        var app = await applications.GetByIdAsync(request.Id, ct);
        if (app is null)
            return Result<GlobalApplicationDto>.Failure("Application not found.", 404);

        var name = request.AppName.Trim();
        if (name.Length is < 1 or > 200)
            return Result<GlobalApplicationDto>.Failure("Application name must contain 1 to 200 characters.", 400);

        var category = string.IsNullOrWhiteSpace(request.Category)
            ? null
            : request.Category.Trim().ToLowerInvariant();
        if (category is not null && !GlobalApplicationCategories.All.Contains(category))
            return Result<GlobalApplicationDto>.Failure("Unknown application category.", 400);

        var publisher = string.IsNullOrWhiteSpace(request.Publisher) ? null : request.Publisher.Trim();
        if (publisher?.Length > 200)
            return Result<GlobalApplicationDto>.Failure("Publisher must not exceed 200 characters.", 400);

        app.AppName = name;
        app.Category = category;
        app.Publisher = publisher;
        app.IsPublic = request.IsPublic;
        app.IsProductiveDefault = request.IsProductiveDefault;
        app.CreatedById ??= request.ActorId;
        app.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
        return Result<GlobalApplicationDto>.Success(GlobalApplicationDto.FromEntity(app));
    }
}
