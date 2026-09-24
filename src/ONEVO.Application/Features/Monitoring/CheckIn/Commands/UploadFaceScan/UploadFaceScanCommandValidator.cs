using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public class UploadFaceScanCommandValidator : AbstractValidator<UploadFaceScanCommand>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public UploadFaceScanCommandValidator()
    {
        RuleFor(x => x.CheckInId)
            .NotEmpty()
            .WithMessage("CheckInId is required.");

        RuleFor(x => x.ContentType)
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Only JPEG, PNG, or WebP images are accepted.");

        RuleFor(x => x.FileSizeBytes)
            .InclusiveBetween(1, MaxFileSizeBytes)
            .WithMessage("Face scan image must be between 1 byte and 5 MB.");
    }
}
