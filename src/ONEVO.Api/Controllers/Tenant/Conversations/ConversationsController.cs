using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Commands.DeleteConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.EditConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.LeaveConversation;
using ONEVO.Application.Features.Conversations.Commands.RemoveConversationParticipant;
using ONEVO.Application.Features.Conversations.Queries.GetConversationAttachment;
using ONEVO.Application.Features.Conversations.Commands.MarkConversationRead;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage;
using ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;
using ONEVO.Application.Features.Conversations.Queries.SearchConversationPeople;

namespace ONEVO.Api.Controllers.Tenant.Conversations;

public sealed record OpenConversationRequest(string Source, string Kind, Guid Id);
public sealed record PostConversationMessageRequest(
    string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds, IReadOnlyList<Guid>? AttachmentFileIds = null, string? Tag = null);
public sealed record EditConversationMessageRequest(string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds);
public sealed record ReactToConversationMessageRequest(string Emoji);
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

    /// <summary>Active colleagues to add to a conversation (name + avatar only), by name.</summary>
    [HttpGet("people")]
    public async Task<IActionResult> People([FromQuery] string? search, CancellationToken ct)
        => ToResult(await _mediator.Send(new SearchConversationPeopleQuery(search), ct));

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(Guid id, [FromQuery] DateTimeOffset? before, [FromQuery] int take = 50, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListConversationMessagesQuery(id, before, take), ct));

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Post(Guid id, [FromBody] PostConversationMessageRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(
            new PostConversationMessageCommand(id, body.BodyHtml, body.MentionEmployeeIds, body.AttachmentFileIds, body.Tag), ct));

    /// <summary>The author edits their own message; the previous text is kept for the investigation export.</summary>
    [HttpPut("{id:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> Edit(Guid id, Guid messageId, [FromBody] EditConversationMessageRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new EditConversationMessageCommand(id, messageId, body.BodyHtml, body.MentionEmployeeIds), ct));

    /// <summary>The author deletes their own message (shown as "Message deleted").</summary>
    [HttpDelete("{id:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid id, Guid messageId, CancellationToken ct)
        => ToNoContent(await _mediator.Send(new DeleteConversationMessageCommand(id, messageId), ct));

    /// <summary>Downloads a chat attachment through the conversation access check (never a public URL).</summary>
    /// <summary>Sets the caller's one reaction on the message (replacing an earlier one).</summary>
    [HttpPut("{id:guid}/messages/{messageId:guid}/reaction")]
    public async Task<IActionResult> React(Guid id, Guid messageId, [FromBody] ReactToConversationMessageRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new ReactToConversationMessageCommand(id, messageId, body.Emoji), ct));

    /// <summary>Removes the caller's reaction on the message.</summary>
    [HttpDelete("{id:guid}/messages/{messageId:guid}/reaction")]
    public async Task<IActionResult> Unreact(Guid id, Guid messageId, CancellationToken ct)
        => ToResult(await _mediator.Send(new ReactToConversationMessageCommand(id, messageId, null), ct));

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Attachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetConversationAttachmentQuery(id, attachmentId), ct);
        return result.IsSuccess
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Removes an added member (base participants, or whoever added them).</summary>
    [HttpDelete("{id:guid}/participants/{employeeId:guid}")]
    public async Task<IActionResult> RemoveParticipant(Guid id, Guid employeeId, CancellationToken ct)
        => ToNoContent(await _mediator.Send(new RemoveConversationParticipantCommand(id, employeeId), ct));

    /// <summary>An added member leaves the conversation.</summary>
    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
        => ToNoContent(await _mediator.Send(new LeaveConversationCommand(id), ct));

    [HttpPost("{id:guid}/participants")]
    public async Task<IActionResult> AddParticipants(Guid id, [FromBody] AddConversationParticipantsRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new AddConversationParticipantsCommand(id, body.EmployeeIds), ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkConversationReadCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private IActionResult ToNoContent(ONEVO.Application.Common.Models.Result result)
        => result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
