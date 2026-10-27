using System.Net;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>Mutes (or unmutes) a chat for the caller only: its unread messages leave their inbox badge.</summary>
public sealed record SetChatMutedCommand(Guid ConversationId, bool Muted) : IRequest<Result>;
/// <summary>Sets a group's (or case group's) description; empty clears it. Plain text.</summary>
public sealed record SetChatDescriptionCommand(Guid ConversationId, string? Description) : IRequest<Result>;
/// <summary>Sets a group's (or case group's) photo from an uploaded conversation_avatar file; null removes it.</summary>
public sealed record SetChatPhotoCommand(Guid ConversationId, Guid? FileId) : IRequest<Result>;
/// <summary>A page of the files posted in a chat. <see cref="Kind"/>: image, video, audio, document, or null for all.</summary>
public sealed record ListChatMediaQuery(Guid ConversationId, string? Kind, int Page, int PageSize) : IRequest<Result<ChatMediaPageResponse>>;
/// <summary>The chat's text messages that contain the text, newest first.</summary>
public sealed record SearchChatMessagesQuery(Guid ConversationId, string Text) : IRequest<Result<IReadOnlyList<ChatSearchResult>>>;

public sealed record ChatMediaItemResponse(
    Guid Id, Guid MessageId, string FileName, string ContentType, long SizeBytes, string Kind, DateTimeOffset SentAt,
    Guid? AuthorEmployeeId, string? AuthorName);
public sealed record ChatMediaPageResponse(
    IReadOnlyList<ChatMediaItemResponse> Items, int Total, int Page, int PageSize, IReadOnlyDictionary<string, int> Counts);
public sealed record ChatSearchResult(Guid MessageId, DateTimeOffset SentAt, Guid? AuthorEmployeeId, string? AuthorName, string Snippet);

public sealed partial class ChatInfoHandlers :
    IRequestHandler<SetChatMutedCommand, Result>,
    IRequestHandler<SetChatDescriptionCommand, Result>,
    IRequestHandler<SetChatPhotoCommand, Result>,
    IRequestHandler<ListChatMediaQuery, Result<ChatMediaPageResponse>>,
    IRequestHandler<SearchChatMessagesQuery, Result<IReadOnlyList<ChatSearchResult>>>
{
    public const int MaxDescription = 500;
    public const int MaxPageSize = 50;
    private const int SearchResults = 30;
    private const int SnippetLength = 140;
    public const string DirectChatMessage = "A 1-on-1 chat has no group settings.";

    private readonly InboxCallerResolver _callers;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly ICallerIdentityResolver _identity;
    private readonly IPrimaryEntityAssetLinker _assets;
    private readonly ICurrentUser _currentUser;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<ChatInfoHandlers> _logger;

    public ChatInfoHandlers(
        InboxCallerResolver callers, IConversationAccess access, IConversationRepository conversations, ICallerIdentityResolver identity,
        IPrimaryEntityAssetLinker assets, ICurrentUser currentUser, IInboxRealtimePublisher realtime, ILogger<ChatInfoHandlers> logger)
    {
        _callers = callers;
        _access = access;
        _conversations = conversations;
        _identity = identity;
        _assets = assets;
        _currentUser = currentUser;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result> Handle(SetChatMutedCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        if (await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct) is null)
            return Result.NotFound(InboxChatHandlers.NotFoundMessage);
        await _conversations.SetMutedAsync(caller.TenantId, caller.EmployeeId, command.ConversationId, command.Muted, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(SetChatDescriptionCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var description = command.Description?.Trim();
        if (description is { Length: > MaxDescription })
            return Result.UnprocessableEntity($"Keep the description under {MaxDescription} characters.");
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result.NotFound(InboxChatHandlers.NotFoundMessage);
        if (context.Conversation.SubjectSource == ConversationSources.Direct)
            return Result.UnprocessableEntity(DirectChatMessage);
        await _conversations.SetDescriptionAsync(caller.TenantId, command.ConversationId, string.IsNullOrEmpty(description) ? null : description, ct);
        await PublishChangedAsync(caller, context, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(SetChatPhotoCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result.NotFound(InboxChatHandlers.NotFoundMessage);
        if (context.Conversation.SubjectSource == ConversationSources.Direct)
            return Result.UnprocessableEntity(DirectChatMessage);
        if (command.FileId is { } fileId && fileId != Guid.Empty)
        {
            var linked = await _assets.LinkAsync(caller.TenantId, _currentUser.UserId, EntityAssetOwnerTypes.Conversation, command.ConversationId,
                UploadPurposeCatalog.ConversationAvatar, fileId, ct);
            if (!linked.IsSuccess)
                return Result.Failure(linked.Error ?? "The photo could not be saved.", linked.StatusCode ?? 400);
        }
        else
        {
            var removed = await _assets.RemoveAsync(caller.TenantId, _currentUser.UserId, EntityAssetOwnerTypes.Conversation, command.ConversationId,
                UploadPurposeCatalog.ConversationAvatar, ct);
            if (!removed.IsSuccess)
                return Result.Failure(removed.Error ?? "The photo could not be removed.", removed.StatusCode ?? 400);
        }
        await PublishChangedAsync(caller, context, ct);
        return Result.Success();
    }

    public async Task<Result<ChatMediaPageResponse>> Handle(ListChatMediaQuery query, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<ChatMediaPageResponse>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.Trim().ToLowerInvariant();
        if (kind is not null && !ConversationMediaKinds.All.Contains(kind))
            return Result<ChatMediaPageResponse>.UnprocessableEntity("Unknown file type.");
        if (await _access.LoadAsync(caller.TenantId, query.ConversationId, caller.EmployeeId, ct) is null)
            return Result<ChatMediaPageResponse>.NotFound(InboxChatHandlers.NotFoundMessage);

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var media = await _conversations.ListMediaAsync(caller.TenantId, query.ConversationId, caller.EmployeeId, kind, (page - 1) * size, size, ct);
        var authors = await _identity.ResolveIdentitiesByEmployeeIdAsync(
            caller.TenantId, media.Items.Select(i => i.AuthorEmployeeId).OfType<Guid>().Distinct().ToList(), ct);
        var items = media.Items.Select(i => new ChatMediaItemResponse(
                i.Attachment.Id, i.Attachment.MessageId, i.Attachment.FileName, i.Attachment.ContentType, i.Attachment.SizeBytes,
                ConversationMediaKinds.Of(i.Attachment.ContentType), i.SentAt, i.AuthorEmployeeId,
                i.AuthorEmployeeId is { } a ? authors.GetValueOrDefault(a)?.Name : null))
            .ToList();
        return Result<ChatMediaPageResponse>.Success(new ChatMediaPageResponse(items, media.Total, page, size, media.Counts));
    }

    public async Task<Result<IReadOnlyList<ChatSearchResult>>> Handle(SearchChatMessagesQuery query, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<IReadOnlyList<ChatSearchResult>>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var text = (query.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            return Result<IReadOnlyList<ChatSearchResult>>.Success([]);
        if (await _access.LoadAsync(caller.TenantId, query.ConversationId, caller.EmployeeId, ct) is null)
            return Result<IReadOnlyList<ChatSearchResult>>.NotFound(InboxChatHandlers.NotFoundMessage);

        // The body is HTML: match the text on the database, then drop hits that were only in the markup.
        var candidates = await _conversations.SearchTextMessagesAsync(caller.TenantId, query.ConversationId, caller.EmployeeId, text, SearchResults * 3, ct);
        var hits = candidates
            .Select(m => (Message: m, Plain: PlainText(m.BodyHtml)))
            .Where(h => h.Plain.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(SearchResults)
            .ToList();
        var authors = await _identity.ResolveIdentitiesByEmployeeIdAsync(
            caller.TenantId, hits.Select(h => h.Message.AuthorEmployeeId).OfType<Guid>().Distinct().ToList(), ct);
        return Result<IReadOnlyList<ChatSearchResult>>.Success(hits
            .Select(h => new ChatSearchResult(h.Message.Id, h.Message.CreatedAt, h.Message.AuthorEmployeeId,
                h.Message.AuthorEmployeeId is { } a ? authors.GetValueOrDefault(a)?.Name : null, Snippet(h.Plain, text)))
            .ToList());
    }

    private async Task PublishChangedAsync(InboxCaller caller, ConversationContext context, CancellationToken ct)
        => await ConversationRules.PublishSafelyAsync(
            () => _realtime.InboxChangedAsync(caller.TenantId, context.ParticipantIds, context.Subject, ct), _logger, "inboxChanged");

    private static string PlainText(string html)
        => WhiteSpace().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    /// <summary>The text around the first match, so the hit is visible in a one-line result.</summary>
    private static string Snippet(string plain, string text)
    {
        if (plain.Length <= SnippetLength)
            return plain;
        var at = plain.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, Math.Min(at - SnippetLength / 3, plain.Length - SnippetLength));
        var cut = plain.Substring(start, SnippetLength).Trim();
        return (start > 0 ? "…" : "") + cut + (start + SnippetLength < plain.Length ? "…" : "");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
