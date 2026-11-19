using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.EnrollFacePhotos;

public class EnrollFacePhotosCommandValidator : AbstractValidator<EnrollFacePhotosCommand>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public EnrollFacePhotosCommandValidator()
    {
        RuleFor(x => x.Front).NotNull().Must(BeValidPhoto).WithMessage(Message("front"));
        RuleFor(x => x.Left).Must(BeValidPhoto).WithMessage(Message("left")).When(x => x.Left is not null);
        RuleFor(x => x.Right).Must(BeValidPhoto).WithMessage(Message("right")).When(x => x.Right is not null);
        RuleFor(x => x)
            .Must(x => (x.Left is null) == (x.Right is null))
            .WithMessage("The left and right photos must be sent together.");
    }

    private static bool BeValidPhoto(FaceSetupPhoto? photo) =>
        photo is not null
        && AllowedContentTypes.Contains(photo.ContentType)
        && photo.FileSizeBytes is >= 1 and <= MaxFileSizeBytes;

    private static string Message(string field) =>
        $"The {field} photo must be a JPEG, PNG, or WebP image between 1 byte and 5 MB.";
}
