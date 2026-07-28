using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.IdentityVerification.Services;

public interface IIdentityImageValidator
{
    Task<Result<byte[]>> ValidateAsync(
        Stream content,
        string fileName,
        string contentType,
        int maximumBytes,
        CancellationToken ct);
}

