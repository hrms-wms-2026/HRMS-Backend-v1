using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.UpdateGlobalApplication;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Queries.GetGlobalApplicationLogo;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Queries.ListGlobalApplications;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.AppCatalog;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/app-catalog")]
public sealed class GlobalAppCatalogController(IMediator mediator, ICurrentPlatformUserContext currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.AppCatalogRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new ListGlobalApplicationsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AppCatalogManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGlobalApplicationRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } actorId) return Forbid();
        var result = await mediator.Send(new UpdateGlobalApplicationCommand(
            id, request.AppName, request.Category, request.Publisher,
            request.IsPublic, request.IsProductiveDefault, actorId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}/logo")]
    [Consumes("multipart/form-data")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AppCatalogManage)]
    public async Task<IActionResult> SetLogo(Guid id, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (currentUser.UserId is not { } actorId) return Forbid();
        if (file is null || file.Length == 0) return Problem("A logo file is required.", statusCode: 400);
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var result = await mediator.Send(new SetGlobalApplicationLogoCommand(id, memory.ToArray(), file.ContentType, actorId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}/logo")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AppCatalogRead)]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetGlobalApplicationLogoQuery(id), ct);
        return result.IsSuccess
            ? File(result.Value!.Content, result.Value.ContentType)
            : Problem(result.Error, statusCode: result.StatusCode ?? 404);
    }
}

public sealed record UpdateGlobalApplicationRequest(
    string AppName,
    string? Category,
    string? Publisher,
    bool IsPublic,
    bool IsProductiveDefault);
