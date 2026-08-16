using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.ListPlatformAnnouncements;

public sealed record ListPlatformAnnouncementsQuery(
    bool? IsPublished,
    string? Severity,
    int Page,
    int PageSize) : IRequest<Result<PlatformAnnouncementListResponseDto>>;
