namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

public sealed record FileUploadReservationDto(
    Guid Id,
    Guid TenantId,
    long ReservedBytes,
    string Status,
    string StorageKey,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
