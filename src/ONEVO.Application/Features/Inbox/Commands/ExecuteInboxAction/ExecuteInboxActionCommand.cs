using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;

/// <summary>Runs one of the item's actions (approve, reject, ...) through its source module.</summary>
public sealed record ExecuteInboxActionCommand(string Source, string Kind, Guid Id, string Action, JsonElement? Body)
    : IRequest<Result<InboxActionResult>>;
