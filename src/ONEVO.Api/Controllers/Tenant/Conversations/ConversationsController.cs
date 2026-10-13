using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Commands.MarkConversationRead;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;
using ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;

namespace ONEVO.Api.Controllers.Tenant.Conversations;

public sealed record OpenConversationRequest(string Source, string Kind, Guid Id);
public sealed record PostConversationMessageRequest(string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds);
public sealed record AddConversationParticipantsRequest(IReadOnlyList<Guid> EmployeeIds);

/// <summary>The group chat of an inbox item. Sender and receiver are base members; any member can add
/// employees; live updates arrive over /hubs/inbox.</summary>
[ApiController]
[Route("api/v1/conversations")]
[Authorize(Policy = "TenantPolicy")]
public class ConversationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConversationsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("open")]
    public async Task<IActionResult> Open([FromBody] OpenConversationRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new OpenConversationCommand(body.Source, body.Kind, body.Id), ct));

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(Guid id, [FromQuery] DateTimeOffset? before, [FromQuery] int take = 50, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListConversationMessagesQuery(id, before, take), ct));

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Post(Guid id, [FromBody] PostConversationMessageRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new PostConversationMessageCommand(id, body.BodyHtml, body.MentionEmployeeIds), ct));

    [HttpPost("{id:guid}/participants")]
    public async Task<IActionResult> AddParticipants(Guid id, [FromBody] AddConversationParticipantsRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new AddConversationParticipantsCommand(id, body.EmployeeIds), ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkConversationReadCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
