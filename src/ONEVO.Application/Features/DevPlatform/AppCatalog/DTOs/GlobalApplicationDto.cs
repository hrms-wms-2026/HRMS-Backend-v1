using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.DTOs;

public sealed record GlobalApplicationDto(
    Guid Id,
    string AppName,
    string ProcessName,
    string? Category,
    bool IsAllocated,
    string? Publisher,
    bool IsPublic,
    bool IsProductiveDefault,
    bool HasLogo,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? UpdatedAt)
{
    public static GlobalApplicationDto FromEntity(GlobalApplication app) => new(
        app.Id,
        app.AppName,
        app.ProcessName,
        app.Category,
        app.IsAllocated,
        app.Publisher,
        app.IsPublic,
        app.IsProductiveDefault,
        app.Logo is { Length: > 0 },
        app.FirstSeenAt,
        app.LastSeenAt,
        app.UpdatedAt);
}
