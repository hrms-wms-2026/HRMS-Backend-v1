using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Admin.Legal;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Legal;

/// <summary>
/// Developer Platform - legal document version management (draft/edit/publish/archive
/// Terms &amp; Privacy). Published content is immutable: only Update on a draft is allowed;
/// publishing a new draft archives the prior published row for the same document_type
/// inside one transaction (see PublishLegalDocumentVersionCommandHandler).
/// SECURITY: reuses the existing platform.compliance.read/manage permissions - no new
/// permission codes were added for this feature.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminLegalDocumentVersionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminLegalDocumentVersionsController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("admin/v1/legal-document-versions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "document_type")] string? documentType,
        [FromQuery(Name = "status")] string? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListLegalDocumentVersionsQuery(documentType, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("admin/v1/legal-document-versions/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLegalDocumentVersionQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(new CreateLegalDocumentVersionCommand(
            request.DocumentType,
            request.Version,
            request.Title,
            ReadContentJsonText(request.ContentJson),
            request.ContentHtml,
            request.ContentText,
            request.IsRequired,
            request.BlockScope,
            actorId.Value), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("admin/v1/legal-document-versions/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateLegalDocumentVersionCommand(
            id,
            request.Title,
            ReadContentJsonText(request.ContentJson),
            request.ContentHtml,
            request.ContentText,
            request.IsRequired,
            request.BlockScope), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions/{id:guid}/publish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(
            new PublishLegalDocumentVersionCommand(id, request.PublishReason, actorId.Value), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions/{id:guid}/archive")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveLegalDocumentVersionCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// An omitted content_json field deserializes to a default (Undefined) JsonElement, and
    /// GetRawText() throws on that - map it to an empty string instead so the command handler's
    /// existing "content_json must not be empty" validation rejects it with 400, not a 500.
    /// </summary>
    private static string ReadContentJsonText(JsonElement contentJson)
    {
        return contentJson.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : contentJson.GetRawText();
    }
}
