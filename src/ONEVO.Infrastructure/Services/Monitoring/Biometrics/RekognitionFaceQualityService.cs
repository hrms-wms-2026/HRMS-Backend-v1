using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceQualityService : IFaceQualityService
{
    private readonly IAwsRekognitionClientFactory _clients;
    private readonly AwsRekognitionOptions _options;

    public RekognitionFaceQualityService(
        IAwsRekognitionClientFactory clients, IOptions<AwsRekognitionOptions> options)
    {
        _clients = clients;
        _options = options.Value;
    }

    public async Task<FaceQualityOutcome> AnalyzeAsync(Stream image, CancellationToken ct)
    {
        var bytes = await ToMemoryStreamAsync(image, ct);
        var rekognition = await _clients.GetRekognitionAsync(ct);
        var response = await rekognition.DetectFacesAsync(new DetectFacesRequest
        {
            Image = new Image { Bytes = bytes },
            Attributes = ["ALL"]
        }, ct);

        var faces = response.FaceDetails ?? [];
        // Zero and several faces are reported separately so the tray can say which one it was.
        if (faces.Count != 1)
            return new FaceQualityOutcome(false, false, false, null, null, faces.Count);

        var face = faces[0];
        var brightness = face.Quality?.Brightness;
        var confidence = face.Confidence;
        var box = face.BoundingBox;
        var pose = face.Pose;

        var lightingOk = brightness is >= 0
            && brightness >= _options.MinBrightness
            && brightness <= _options.MaxBrightness;

        var boxOk = box is not null
            && (box.Width ?? 0f) >= _options.MinFaceBoxRatio
            && (box.Height ?? 0f) >= _options.MinFaceBoxRatio;

        var poseOk = pose is null
            || (Abs(pose.Yaw) <= _options.MaxHeadPoseDegrees
                && Abs(pose.Pitch) <= _options.MaxHeadPoseDegrees);

        var faceVisible = (confidence ?? 0f) >= _options.MinFaceConfidence && boxOk && poseOk;

        var sunglasses = IsFlagged(face.Sunglasses?.Value, face.Sunglasses?.Confidence);
        var occluded = IsFlagged(face.FaceOccluded?.Value, face.FaceOccluded?.Confidence);
        var noSunglassesOrMask = !sunglasses && !occluded;

        return new FaceQualityOutcome(lightingOk, faceVisible, noSunglassesOrMask, brightness, confidence);
    }

    private bool IsFlagged(bool? value, float? confidence) =>
        value == true && (confidence ?? 0f) >= _options.AccessoryConfidenceThreshold;

    private static float Abs(float? value) => Math.Abs(value ?? 0f);

    private static async Task<MemoryStream> ToMemoryStreamAsync(Stream input, CancellationToken ct)
    {
        var copy = new MemoryStream();
        await input.CopyToAsync(copy, ct);
        copy.Position = 0;
        return copy;
    }
}
