using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Inbox.Chats;

namespace ONEVO.Api.Controllers.Tenant.Inbox;

public sealed record OpenDirectChatRequest(Guid EmployeeId);
public sealed record CreateGroupChatRequest(string Name, IReadOnlyList<Guid> EmployeeIds);
public sealed record RenameGroupChatRequest(string Name);
public sealed record SetChatFavoriteRequest(bool Favorite);
public sealed record StartCaseGroupRequest(string Source, string Kind, Guid Id, IReadOnlyList<Guid>? EmployeeIds);

/// <summary>The inbox as chats: 1-on-1 chats with anyone in the legal entity, named groups, and the dedicated group
/// of a case. Requests and alerts arrive as case cards inside them. Messages go through /conversations.</summary>
[ApiController]
[Route("api/v1/inbox/chats")]
[Authorize(Policy = "TenantPolicy")]
public class InboxChatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InboxChatsController(IMediator mediator) => _mediator = mediator;

    /// <summary>The caller's chats, newest first (chats with something waiting on them first). Placing new case
    /// cards happens here, from the caller's own inbox.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => ToResult(await _mediator.Send(new ListInboxChatsQuery(), ct));

    /// <summary>The chat where the caller works on an inbox item (deep links, the bell).</summary>
    [HttpGet("for-item/{source}/{kind}/{id:guid}")]
    public async Task<IActionResult> ForItem(string source, string kind, Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetInboxChatForItemQuery(source, kind, id), ct));

    [HttpPost("direct")]
    public async Task<IActionResult> OpenDirect([FromBody] OpenDirectChatRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new OpenDirectChatCommand(body.EmployeeId), ct));

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupChatRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new CreateGroupChatCommand(body.Name, body.EmployeeIds ?? []), ct));

    [HttpPut("groups/{conversationId:guid}/name")]
    public async Task<IActionResult> RenameGroup(Guid conversationId, [FromBody] RenameGroupChatRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RenameGroupChatCommand(conversationId, body.Name), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Pins (or unpins) one of the caller's chats to the top of its section, for them only.</summary>
    [HttpPut("{conversationId:guid}/favorite")]
    public async Task<IActionResult> SetFavorite(Guid conversationId, [FromBody] SetChatFavoriteRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetChatFavoriteCommand(conversationId, body.Favorite), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Moves a case to its own group (created once) and adds people to it.</summary>
    [HttpPost("case-group")]
    public async Task<IActionResult> StartCaseGroup([FromBody] StartCaseGroupRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new StartCaseGroupCommand(body.Source, body.Kind, body.Id, body.EmployeeIds ?? []), ct));

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
