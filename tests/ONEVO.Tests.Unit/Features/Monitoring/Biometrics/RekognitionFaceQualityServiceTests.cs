using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class RekognitionFaceQualityServiceTests
{
    private readonly Mock<IAmazonRekognition> _rekognition = new();
    private readonly AwsRekognitionOptions _options = new()
    {
        Region = "us-east-1",
        LivenessRoleArn = "arn:aws:iam::123456789012:role/liveness",
        MinBrightness = 40f,
        MaxBrightness = 95f,
        MinFaceConfidence = 90f,
        MinFaceBoxRatio = 0.12f,
        MaxHeadPoseDegrees = 35f,
        AccessoryConfidenceThreshold = 80f
    };

    private RekognitionFaceQualityService CreateSut() =>
        new(new StubRekognitionClientFactory(_rekognition.Object), Options.Create(_options));

    private static MemoryStream Bytes() => new(new byte[] { 1, 2, 3 });

    private static FaceDetail GoodFace(
        float brightness = 70f,
        bool sunglasses = false,
        bool occluded = false,
        float confidence = 99f,
        float width = 0.4f,
        float yaw = 0f) => new()
    {
        Confidence = confidence,
        BoundingBox = new BoundingBox { Width = width, Height = 0.5f },
        Quality = new ImageQuality { Brightness = brightness, Sharpness = 50f },
        Sunglasses = new Sunglasses { Value = sunglasses, Confidence = 99f },
        FaceOccluded = new FaceOccluded { Value = occluded, Confidence = 99f },
        Pose = new Pose { Yaw = yaw, Pitch = 0f, Roll = 0f }
    };

    private void SetupFaces(params FaceDetail[] faces)
    {
        _rekognition.Setup(r => r.DetectFacesAsync(
                It.Is<DetectFacesRequest>(req => req.Attributes.Contains("ALL")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DetectFacesResponse { FaceDetails = [.. faces] });
    }

    [Fact]
    public async Task SingleClearFace_PassesAllChecks()
    {
        SetupFaces(GoodFace());

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.LightingOk.Should().BeTrue();
        result.FaceVisible.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeTrue();
        result.Brightness.Should().Be(70f);
        result.FaceConfidence.Should().Be(99f);
    }

    [Fact]
    public async Task NoFaces_FailsAllChecks()
    {
        SetupFaces();

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.LightingOk.Should().BeFalse();
        result.FaceVisible.Should().BeFalse();
        result.NoSunglassesOrMask.Should().BeFalse();
    }

    [Fact]
    public async Task TwoFaces_FailsAllChecks()
    {
        SetupFaces(GoodFace(), GoodFace(width: 0.3f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceVisible.Should().BeFalse();
        result.LightingOk.Should().BeFalse();
        result.NoSunglassesOrMask.Should().BeFalse();
    }

    [Fact]
    public async Task TwoFaces_ReportsFaceCountTwo()
    {
        SetupFaces(GoodFace(), GoodFace(width: 0.3f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceCount.Should().Be(2);
    }

    [Fact]
    public async Task EmployeePlusTinyBackgroundFace_IgnoresBackgroundFace()
    {
        // e.g. a photo on a monitor behind the employee.
        SetupFaces(GoodFace(width: 0.05f, confidence: 97f), GoodFace());

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceCount.Should().Be(1);
        result.FaceVisible.Should().BeTrue();
        result.LightingOk.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeTrue();
        // Both faces are still reported for diagnosis, even the ignored background one.
        result.Faces.Should().HaveCount(2);
    }

    [Fact]
    public async Task EmployeePlusLowConfidenceGhostFace_IgnoresGhostFace()
    {
        SetupFaces(GoodFace(), GoodFace(width: 0.2f, confidence: 55f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceCount.Should().Be(1);
        result.FaceVisible.Should().BeTrue();
    }

    [Fact]
    public async Task NoFaces_ReportsFaceCountZero()
    {
        SetupFaces();

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceCount.Should().Be(0);
    }

    [Fact]
    public async Task LowBrightness_FailsLightingOnly()
    {
        SetupFaces(GoodFace(brightness: 20f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.LightingOk.Should().BeFalse();
        result.FaceVisible.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeTrue();
    }

    [Fact]
    public async Task Sunglasses_FailsObstructionOnly()
    {
        SetupFaces(GoodFace(sunglasses: true));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.NoSunglassesOrMask.Should().BeFalse();
        result.LightingOk.Should().BeTrue();
        result.FaceVisible.Should().BeTrue();
    }

    [Fact]
    public async Task MaskOrOcclusion_FailsObstructionOnly()
    {
        SetupFaces(GoodFace(occluded: true));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.NoSunglassesOrMask.Should().BeFalse();
        result.LightingOk.Should().BeTrue();
        result.FaceVisible.Should().BeTrue();
    }

    [Fact]
    public async Task TinyFace_FailsFaceVisible()
    {
        SetupFaces(GoodFace(width: 0.05f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FaceVisible.Should().BeFalse();
        result.LightingOk.Should().BeTrue();
    }

    [Fact]
    public async Task LookingStraight_IsFacingFront_NotSideways()
    {
        SetupFaces(GoodFace(yaw: 4f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.FacingFront.Should().BeTrue();
        result.TurnedSideways.Should().BeFalse();
        result.Yaw.Should().Be(4f);
    }

    [Theory]
    [InlineData(25f)]
    [InlineData(-25f)]
    public async Task HeadTurned_IsSideways_AndStillVisible(float yaw)
    {
        SetupFaces(GoodFace(yaw: yaw));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.TurnedSideways.Should().BeTrue();
        result.FacingFront.Should().BeFalse();
        result.FaceVisible.Should().BeTrue();
        result.Yaw.Should().Be(yaw);
    }

    [Fact]
    public async Task HeadTurnedTooFar_IsNotAUsableSidePhoto()
    {
        SetupFaces(GoodFace(yaw: 50f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.TurnedSideways.Should().BeFalse();
        result.FaceVisible.Should().BeFalse();
    }

    private static FaceDetail WithEyes(FaceDetail face, bool glasses, bool eyesOpen, float eyesConfidence)
    {
        face.Eyeglasses = new Eyeglasses { Value = glasses, Confidence = 99f };
        face.EyesOpen = new EyeOpen { Value = eyesOpen, Confidence = eyesConfidence };
        return face;
    }

    [Fact]
    public async Task EyesOpenWithLowConfidence_Passes()
    {
        // Real clock-in photo from the tray: eyes open at 59.5% confidence, no glasses detected.
        SetupFaces(WithEyes(GoodFace(yaw: 3.9f), glasses: false, eyesOpen: true, eyesConfidence: 59.5f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.EyesClosed.Should().BeFalse();
        result.GlassesGlare.Should().BeFalse();
        result.FaceVisible.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeTrue();
    }

    [Fact]
    public async Task EyesClosedButUnsure_Passes()
    {
        SetupFaces(WithEyes(GoodFace(), glasses: false, eyesOpen: false, eyesConfidence: 70f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.EyesClosed.Should().BeFalse();
        result.FaceVisible.Should().BeTrue();
    }

    [Fact]
    public async Task GlassesWithEyesHidden_IsGlare_AndFailsObstruction()
    {
        SetupFaces(WithEyes(GoodFace(), glasses: true, eyesOpen: false, eyesConfidence: 95f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.GlassesGlare.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeFalse();
        result.FaceVisible.Should().BeTrue();
        result.LightingOk.Should().BeTrue();
    }

    [Fact]
    public async Task GlassesWithEyesClearlyOpen_Passes()
    {
        SetupFaces(WithEyes(GoodFace(), glasses: true, eyesOpen: true, eyesConfidence: 95f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.GlassesGlare.Should().BeFalse();
        result.NoSunglassesOrMask.Should().BeTrue();
    }

    [Fact]
    public async Task NoGlassesEyesClosed_IsEyesClosed_NotGlare()
    {
        SetupFaces(WithEyes(GoodFace(), glasses: false, eyesOpen: false, eyesConfidence: 95f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.EyesClosed.Should().BeTrue();
        result.GlassesGlare.Should().BeFalse();
        result.FaceVisible.Should().BeFalse();
    }

    [Fact]
    public async Task TurnedHead_LowEyesReading_IsNotGlare()
    {
        SetupFaces(WithEyes(GoodFace(yaw: 25f), glasses: true, eyesOpen: false, eyesConfidence: 95f));

        var result = await CreateSut().AnalyzeAsync(Bytes(), CancellationToken.None);

        result.GlassesGlare.Should().BeFalse();
        result.TurnedSideways.Should().BeTrue();
        result.NoSunglassesOrMask.Should().BeTrue();
    }
}
