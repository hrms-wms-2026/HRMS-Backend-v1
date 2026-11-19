using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceQualityService : IFaceQualityService
{
    private readonly IAwsRekognitionClientFactory _clients;
    private readonly AwsRekognitionOptions _options;
    private readonly ILogger<RekognitionFaceQualityService> _logger;

    public RekognitionFaceQualityService(
        IAwsRekognitionClientFactory clients,
        IOptions<AwsRekognitionOptions> options,
        ILogger<RekognitionFaceQualityService>? logger = null)
    {
        _clients = clients;
        _options = options.Value;
        _logger = logger ?? NullLogger<RekognitionFaceQualityService>.Instance;
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
        _logger.LogInformation(
            "DetectFaces returned {Count} face(s): {Faces}",
            faces.Count,
            string.Join("; ", faces.Select(f =>
                $"conf={f.Confidence:0.#} x={f.BoundingBox?.Left:0.###} y={f.BoundingBox?.Top:0.###} " +
                $"w={f.BoundingBox?.Width:0.###} h={f.BoundingBox?.Height:0.###}")));

        var detected = faces
            .Select(f => new DetectedFace(
                f.Confidence ?? 0f,
                f.BoundingBox?.Left ?? 0f,
                f.BoundingBox?.Top ?? 0f,
                f.BoundingBox?.Width ?? 0f,
                f.BoundingBox?.Height ?? 0f))
            .ToList();

        if (faces.Count == 0)
            return new FaceQualityOutcome(false, false, false, null, null, FaceCount: 0, Faces: detected);

        // The employee is the largest face. DetectFaces also reports tiny or low-confidence
        // "faces" in the background (a photo on a monitor, a poster, a reflection), which must
        // not fail an employee sitting alone. Only a second clearly visible, reasonably sized
        // face counts as another person in the frame.
        var face = faces.OrderByDescending(BoxArea).First();
        var otherPeople = faces.Count(f => !ReferenceEquals(f, face) && IsSignificantFace(f));
        if (otherPeople > 0)
            return new FaceQualityOutcome(false, false, false, null, null, FaceCount: 1 + otherPeople, Faces: detected);
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

        // Rekognition has no glare attribute. Light reflecting on glasses hides the eyes, which
        // Rekognition reads as "eyes closed" — with glasses on that is reported as glare,
        // without glasses as closed eyes. Only a confident "closed" counts: a low-confidence
        // "open" is normal for real employees with their eyes open (and Eyeglasses is often
        // missed), so treating it as hidden eyes would reject good photos.
        // Only judged when looking roughly straight: a turned head (face setup side photos)
        // naturally lowers the eyes reading.
        var glasses = IsFlagged(face.Eyeglasses?.Value, face.Eyeglasses?.Confidence);
        var lookingStraight = Abs(pose?.Yaw) < _options.SideMinYawDegrees;
        var eyesVisible = !lookingStraight
            || face.EyesOpen is null
            || face.EyesOpen.Value != false
            || (face.EyesOpen.Confidence ?? 0f) < _options.MinEyesClosedConfidence;
        var glassesGlare = glasses && !sunglasses && !eyesVisible;
        var eyesClosed = !glasses && !sunglasses && !eyesVisible;
        if (eyesClosed)
            faceVisible = false;

        var noSunglassesOrMask = !sunglasses && !occluded && !glassesGlare;

        // Face setup poses. Only the magnitude is judged: Rekognition's yaw sign and a mirrored
        // preview make "left"/"right" unreliable, so setup requires the two side shots to have
        // opposite signs instead.
        var yaw = pose?.Yaw;
        var absYaw = Abs(yaw);
        var facingFront = pose is not null && absYaw <= _options.FrontMaxYawDegrees;
        var turnedSideways = pose is not null
            && absYaw >= _options.SideMinYawDegrees
            && absYaw <= _options.MaxHeadPoseDegrees;

        _logger.LogInformation(
            "Face quality: brightness={Brightness:0.#} confidence={Confidence:0.#} yaw={Yaw:0.#} pitch={Pitch:0.#} " +
            "lighting={Lighting} visible={Visible} unobstructed={Unobstructed} glasses={Glasses} " +
            "eyesOpen={EyesOpen}/{EyesOpenConfidence:0.#}",
            brightness, confidence, yaw, pose?.Pitch, lightingOk, faceVisible, noSunglassesOrMask, glasses,
            face.EyesOpen?.Value, face.EyesOpen?.Confidence);

        return new FaceQualityOutcome(
            lightingOk, faceVisible, noSunglassesOrMask, brightness, confidence,
            Yaw: yaw, FacingFront: facingFront, TurnedSideways: turnedSideways,
            GlassesGlare: glassesGlare, EyesClosed: eyesClosed, Faces: detected);
    }

    private static float BoxArea(FaceDetail f) =>
        (f.BoundingBox?.Width ?? 0f) * (f.BoundingBox?.Height ?? 0f);

    private bool IsSignificantFace(FaceDetail f) =>
        (f.Confidence ?? 0f) >= _options.MinFaceConfidence
        && (f.BoundingBox?.Width ?? 0f) >= _options.MinFaceBoxRatio
        && (f.BoundingBox?.Height ?? 0f) >= _options.MinFaceBoxRatio;

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
