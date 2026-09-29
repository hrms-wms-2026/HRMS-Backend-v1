using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

public interface IAvatarImageProcessor
{
    /// <summary>
    /// Validates and normalizes an employee avatar to the canonical stored format.
    /// The returned stream is owned by the caller.
    /// </summary>
    Task<Result<ProcessedAvatarFileDto>> ProcessAsync(
        string originalFileName,
        Stream content,
        CancellationToken ct = default);
}
