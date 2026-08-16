using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.UnpublishPlatformAnnouncement;

public sealed record UnpublishPlatformAnnouncementCommand(Guid AnnouncementId) : IRequest<Result<PlatformAnnouncementDto>>;
