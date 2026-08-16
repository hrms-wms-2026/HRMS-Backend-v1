using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.PublishPlatformAnnouncement;

public sealed record PublishPlatformAnnouncementCommand(Guid AnnouncementId) : IRequest<Result<PlatformAnnouncementDto>>;
