using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.CreatePlatformAnnouncement;

public sealed record CreatePlatformAnnouncementCommand(
    string Title,
    string Body,
    string? Severity,
    string? Audience) : IRequest<Result<PlatformAnnouncementDto>>;
