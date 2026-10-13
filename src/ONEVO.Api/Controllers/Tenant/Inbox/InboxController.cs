using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxCounts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxFilters;
using ONEVO.Application.Features.Inbox.Queries.GetInboxItemDetail;
using ONEVO.Application.Features.Inbox.Queries.ListInboxItems;

namespace ONEVO.Api.Controllers.Tenant.Inbox;

/// <summary>Query-string shape of GET /inbox. Repeat a key for several values (?status=pending&amp;status=open);
/// source-specific filters go in <c>facet</c> as "key:value" (?facet=module:{id}).</summary>
public sealed class InboxListRequest
{
    public string[]? Category { get; set; }
    public string[]? Source { get; set; }
    public Guid[]? Project { get; set; }
    public string[]? Status { get; set; }
    public string[]? Direction { get; set; }
    public string[]? Type { get; set; }
    public Guid[]? RequestedBy { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Search { get; set; }
    public string[]? Facet { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public InboxQuery ToQuery() => new(
        Category, Source, Project, Status, Direction, Type, RequestedBy, From, To, Search, ParseFacets(Facet), Page, PageSize);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? ParseFacets(string[]? facets)
        => facets is not { Length: > 0 }
            ? null
            : facets.Select(f => f.Split(':', 2))
                .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                .GroupBy(parts => parts[0])
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(parts => parts[1]).Distinct().ToList());
}

/// <summary>The user's Inbox across every module (Work Management today; Leave/HR later) through the
/// inbox router. Items are addressed as {source}/{kind}/{id}.</summary>
[ApiController]
[Route("api/v1/inbox")]
[Authorize(Policy = "TenantPolicy")]
public class InboxController : ControllerBase
{
    private readonly IMediator _mediator;

    public InboxController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InboxListRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new ListInboxItemsQuery(request.ToQuery()), ct));

    [HttpGet("filters")]
    public async Task<IActionResult> Filters(CancellationToken ct)
        => ToResult(await _mediator.Send(new GetInboxFiltersQuery(), ct));

    [HttpGet("counts")]
    public async Task<IActionResult> Counts(CancellationToken ct)
        => ToResult(await _mediator.Send(new GetInboxCountsQuery(), ct));

    [HttpGet("{source}/{kind}/{id:guid}")]
    public async Task<IActionResult> Detail(string source, string kind, Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetInboxItemDetailQuery(source, kind, id), ct));

    /// <summary>Runs an action listed in the item's availableActions. Body is action-specific, e.g.
    /// {"comment": "...", "editedPayloadJson": "..."} for approve.</summary>
    [HttpPost("{source}/{kind}/{id:guid}/actions/{action}")]
    public async Task<IActionResult> Execute(
        string source, string kind, Guid id, string action,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body, CancellationToken ct)
        => ToResult(await _mediator.Send(new ExecuteInboxActionCommand(source, kind, id, action, body), ct));

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
