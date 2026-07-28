using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;

namespace ONEVO.Api.Controllers.Tenant.Legal;

/// <summary>
/// Anonymous read of published legal document content. legal_document_versions has no
/// tenant_id and no RLS policy (only legal_acceptance_records is tenant-owned), so these
/// queries are safe with no tenant context resolved. Never returns draft/archived content
/// or any tenant/user identifier - only status=published rows via GetPublishedAsync /
/// GetCurrentRequiredVersionsAsync, both of which hard-filter on status.
/// </summary>
[ApiController]
public sealed class LegalDocumentContentController : ControllerBase
{
    private readonly IMediator _mediator;

    public LegalDocumentContentController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("api/v1/legal/documents/current")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCurrentPublishedLegalDocumentsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("api/v1/legal/documents/{documentType}/{version}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByVersion(string documentType, string version, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPublishedLegalDocumentQuery(documentType, version), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
