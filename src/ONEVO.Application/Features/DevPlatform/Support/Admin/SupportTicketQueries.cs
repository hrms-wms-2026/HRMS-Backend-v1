using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Admin;

/// <summary>Platform staff ticket list across every tenant. assignee "unassigned" lists tickets with no owner.</summary>
public sealed record ListSupportTicketsQuery(
    string? Status,
    string? Priority,
    Guid? TenantId,
    Guid? AssignedToPlatformUserId,
    bool UnassignedOnly,
    string? Search,
    int? Page,
    int? PageSize,
    bool AwaitingSupportOnly = false) : IRequest<Result<SupportPageDto<SupportTicketDto>>>;

public sealed class ListSupportTicketsQueryHandler
    : IRequestHandler<ListSupportTicketsQuery, Result<SupportPageDto<SupportTicketDto>>>
{
    private readonly ISupportTicketRepository _repository;

    public ListSupportTicketsQueryHandler(ISupportTicketRepository repository) => _repository = repository;

    public async Task<Result<SupportPageDto<SupportTicketDto>>> Handle(
        ListSupportTicketsQuery request, CancellationToken cancellationToken)
    {
        if (request.Status is not null && !SupportTicket.Statuses.Contains(request.Status))
        {
            return Result<SupportPageDto<SupportTicketDto>>.Failure(
                $"status must be one of: {string.Join(", ", SupportTicket.Statuses)}.");
        }

        if (request.Priority is not null && !SupportTicket.Priorities.Contains(request.Priority))
        {
            return Result<SupportPageDto<SupportTicketDto>>.Failure(
                $"priority must be one of: {string.Join(", ", SupportTicket.Priorities)}.");
        }

        var (page, pageSize) = SupportTicketRules.NormalizePaging(request.Page, request.PageSize);
        var (items, total) = await _repository.ListAsync(
            new SupportTicketFilter(
                request.TenantId, request.Status, request.Priority, request.AssignedToPlatformUserId,
                request.UnassignedOnly, request.Search, page, pageSize, request.AwaitingSupportOnly),
            cancellationToken);

        var dtos = items.Select(r => SupportMapper.ToDto(r.Ticket, r.TenantName, r.AssignedToName)).ToList();
        return Result<SupportPageDto<SupportTicketDto>>.Success(new SupportPageDto<SupportTicketDto>(dtos, total, page, pageSize));
    }
}

/// <summary>Counts for the admin console's support bell, from the caller's point of view.</summary>
public sealed record GetSupportSummaryQuery(Guid ActorPlatformUserId) : IRequest<Result<SupportSummaryDto>>;

public sealed class GetSupportSummaryQueryHandler : IRequestHandler<GetSupportSummaryQuery, Result<SupportSummaryDto>>
{
    private readonly ISupportTicketRepository _repository;

    public GetSupportSummaryQueryHandler(ISupportTicketRepository repository) => _repository = repository;

    public async Task<Result<SupportSummaryDto>> Handle(GetSupportSummaryQuery request, CancellationToken cancellationToken)
    {
        var counts = await _repository.CountAsync(request.ActorPlatformUserId, cancellationToken);
        return Result<SupportSummaryDto>.Success(
            new SupportSummaryDto(counts.AwaitingSupport, counts.AwaitingSupportAssignedTo, counts.OpenUnassigned));
    }
}

public sealed record GetSupportTicketQuery(Guid TicketId) : IRequest<Result<SupportTicketDetailDto>>;

public sealed class GetSupportTicketQueryHandler : IRequestHandler<GetSupportTicketQuery, Result<SupportTicketDetailDto>>
{
    private readonly ISupportTicketRepository _repository;

    public GetSupportTicketQueryHandler(ISupportTicketRepository repository) => _repository = repository;

    public async Task<Result<SupportTicketDetailDto>> Handle(GetSupportTicketQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _repository.GetWithChildrenAsync(request.TicketId, null, cancellationToken);
        if (ticket is null)
        {
            return Result<SupportTicketDetailDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");
        }

        return Result<SupportTicketDetailDto>.Success(
            await SupportTicketLabels.ToDetailAsync(_repository, ticket, cancellationToken));
    }
}

public sealed record ListSupportAssigneesQuery : IRequest<Result<IReadOnlyList<SupportAssigneeDto>>>;

public sealed class ListSupportAssigneesQueryHandler
    : IRequestHandler<ListSupportAssigneesQuery, Result<IReadOnlyList<SupportAssigneeDto>>>
{
    private readonly ISupportTicketRepository _repository;

    public ListSupportAssigneesQueryHandler(ISupportTicketRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SupportAssigneeDto>>> Handle(
        ListSupportAssigneesQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repository.ListActivePlatformUsersAsync(cancellationToken);
        IReadOnlyList<SupportAssigneeDto> dtos = rows.Select(r => new SupportAssigneeDto(r.Id, r.FullName, r.Email)).ToList();
        return Result<IReadOnlyList<SupportAssigneeDto>>.Success(dtos);
    }
}

public sealed record GetSupportAttachmentDownloadUrlQuery(Guid TicketId, Guid AttachmentId)
    : IRequest<Result<SupportAttachmentDownloadDto>>;

public sealed class GetSupportAttachmentDownloadUrlQueryHandler
    : IRequestHandler<GetSupportAttachmentDownloadUrlQuery, Result<SupportAttachmentDownloadDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportAttachmentService _attachments;

    public GetSupportAttachmentDownloadUrlQueryHandler(ISupportTicketRepository repository, ISupportAttachmentService attachments)
    {
        _repository = repository;
        _attachments = attachments;
    }

    public async Task<Result<SupportAttachmentDownloadDto>> Handle(
        GetSupportAttachmentDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentAsync(request.TicketId, request.AttachmentId, null, cancellationToken);
        if (attachment is null)
        {
            return Result<SupportAttachmentDownloadDto>.NotFound($"Attachment '{request.AttachmentId}' was not found.");
        }

        return Result<SupportAttachmentDownloadDto>.Success(await _attachments.GetDownloadUrlAsync(attachment, cancellationToken));
    }
}

/// <summary>Resolves the tenant and assignee labels for a single admin ticket response.</summary>
internal static class SupportTicketLabels
{
    public static async Task<SupportTicketDto> ToDtoAsync(ISupportTicketRepository repository, SupportTicket ticket, CancellationToken ct)
    {
        var (tenantName, assigneeName) = await ResolveAsync(repository, ticket, ct);
        return SupportMapper.ToDto(ticket, tenantName, assigneeName);
    }

    public static async Task<SupportTicketDetailDto> ToDetailAsync(
        ISupportTicketRepository repository, SupportTicket ticket, CancellationToken ct)
    {
        var (tenantName, assigneeName) = await ResolveAsync(repository, ticket, ct);
        return SupportMapper.ToDetailDto(ticket, tenantName, assigneeName);
    }

    private static async Task<(string TenantName, string? AssigneeName)> ResolveAsync(
        ISupportTicketRepository repository, SupportTicket ticket, CancellationToken ct)
    {
        var tenantName = await repository.GetTenantNameAsync(ticket.TenantId, ct) ?? string.Empty;
        var assigneeName = ticket.AssignedToPlatformUserId is { } assignee
            ? await repository.GetPlatformUserNameAsync(assignee, ct)
            : null;
        return (tenantName, assigneeName);
    }
}
