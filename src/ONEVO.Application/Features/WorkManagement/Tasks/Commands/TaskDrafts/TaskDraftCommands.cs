using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.TaskDrafts;

public static class TaskDraftLimits
{
    public const int MaxPayloadLength = 262144;
    public const int MaxTitleLength = 500;
}

public sealed record SaveTaskDraftCommand(Guid? DraftId, Guid ProjectId, string? Title, string PayloadJson)
    : IRequest<Result<TaskDraftResponse>>;

public sealed record DeleteTaskDraftCommand(Guid DraftId) : IRequest<Result>;

public sealed class SaveTaskDraftCommandValidator : AbstractValidator<SaveTaskDraftCommand>
{
    public SaveTaskDraftCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(TaskDraftLimits.MaxTitleLength);
        RuleFor(x => x.PayloadJson).NotEmpty().MaximumLength(TaskDraftLimits.MaxPayloadLength)
            .Must(BeJsonObject).WithMessage("Payload must be a JSON object.");
    }

    private static bool BeJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class SaveTaskDraftCommandHandler(ITaskDraftRepository drafts, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<SaveTaskDraftCommand, Result<TaskDraftResponse>>
{
    public async Task<Result<TaskDraftResponse>> Handle(SaveTaskDraftCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result<TaskDraftResponse>.Forbidden("Authentication required.");
        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        TaskDraft draft;
        if (request.DraftId is { } id)
        {
            var existing = await drafts.GetOwnedAsync(tenantId, userId, id, ct);
            if (existing is null) return Result<TaskDraftResponse>.NotFound("Draft not found.");
            draft = existing;
        }
        else
        {
            draft = new TaskDraft { Id = Guid.NewGuid(), TenantId = tenantId, OwnerUserId = userId, CreatedById = userId, CreatedAt = now };
            await drafts.AddAsync(draft, ct);
        }

        draft.ProjectId = request.ProjectId;
        draft.Title = request.Title?.Trim() ?? string.Empty;
        draft.PayloadJson = request.PayloadJson;
        draft.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        return Result<TaskDraftResponse>.Success(new TaskDraftResponse(draft.Id, draft.ProjectId, draft.Title, draft.PayloadJson, now));
    }
}

public sealed class DeleteTaskDraftCommandHandler(
    ITaskDraftRepository drafts, IUnitOfWork unitOfWork, ICurrentUser currentUser, IMediator mediator,
    ILogger<DeleteTaskDraftCommandHandler> logger)
    : IRequestHandler<DeleteTaskDraftCommand, Result>
{
    public async Task<Result> Handle(DeleteTaskDraftCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result.Forbidden("Authentication required.");
        var draft = await drafts.GetOwnedAsync(currentUser.TenantId, currentUser.UserId, request.DraftId, ct);
        if (draft is null) return Result.NotFound("Draft not found.");

        // Pending uploads referenced by the draft are only kept alive by it. Files already attached to a
        // created task make DeleteTaskPendingUpload return Conflict - that is expected and ignored.
        foreach (var fileId in ReadAttachmentFileIds(draft.PayloadJson))
        {
            try
            {
                await mediator.Send(new DeleteTaskPendingUploadCommand(fileId), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete pending upload {FileId} for task draft {DraftId}", fileId, draft.Id);
            }
        }

        drafts.Remove(draft);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static IEnumerable<Guid> ReadAttachmentFileIds(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("attachmentFileIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
                return [];
            return ids.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
