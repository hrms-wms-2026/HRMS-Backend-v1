namespace ONEVO.Application.Common.ServiceInterfaces;

public record FaceMatchOutcome(bool IsMatch, float Similarity);

public interface IFaceMatchService
{
    /// <summary>
    /// Compares a reference face photo against a newly captured photo.
    /// Both streams must be readable from position 0; neither is disposed by this call.
    /// </summary>
    Task<FaceMatchOutcome> CompareAsync(Stream referenceImage, Stream capturedImage, CancellationToken ct);
}
