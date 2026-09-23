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
        float width = 0.4f) => new()
    {
        Confidence = confidence,
        BoundingBox = new BoundingBox { Width = width, Height = 0.5f },
        Quality = new ImageQuality { Brightness = brightness, Sharpness = 50f },
        Sunglasses = new Sunglasses { Value = sunglasses, Confidence = 99f },
        FaceOccluded = new FaceOccluded { Value = occluded, Confidence = 99f },
        Pose = new Pose { Yaw = 0f, Pitch = 0f, Roll = 0f }
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
}
