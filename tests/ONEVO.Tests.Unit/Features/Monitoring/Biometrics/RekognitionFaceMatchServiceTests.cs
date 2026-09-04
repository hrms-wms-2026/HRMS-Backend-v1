using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class RekognitionFaceMatchServiceTests
{
    private readonly Mock<IAmazonRekognition> _rekognition = new();
    private readonly AwsRekognitionOptions _options = new()
    {
        Region = "us-east-1",
        LivenessRoleArn = "arn:aws:iam::123456789012:role/liveness",
        FaceMatchSimilarityThreshold = 80f
    };

    private RekognitionFaceMatchService CreateSut() => new(_rekognition.Object, Options.Create(_options));

    private static MemoryStream Bytes(params byte[] b) => new(b.Length == 0 ? new byte[] { 1, 2, 3 } : b);

    [Fact]
    public async Task MatchAboveThreshold_ReturnsIsMatchTrue_WithHighestSimilarity()
    {
        _rekognition.Setup(r => r.CompareFacesAsync(
                It.Is<CompareFacesRequest>(req => req.SimilarityThreshold == 80f),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompareFacesResponse
            {
                FaceMatches = new List<CompareFacesMatch>
                {
                    new() { Similarity = 91.2f },
                    new() { Similarity = 96.8f }
                }
            });

        var result = await CreateSut().CompareAsync(Bytes(), Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeTrue();
        result.Similarity.Should().Be(96.8f);
    }

    [Fact]
    public async Task NoFaceMatches_ReturnsIsMatchFalse_WithZeroSimilarity()
    {
        _rekognition.Setup(r => r.CompareFacesAsync(It.IsAny<CompareFacesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompareFacesResponse { FaceMatches = new List<CompareFacesMatch>() });

        var result = await CreateSut().CompareAsync(Bytes(), Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeFalse();
        result.Similarity.Should().Be(0f);
    }

    [Fact]
    public async Task NonMemoryStreamInput_IsBufferedAndComparedCorrectly()
    {
        CompareFacesRequest? capturedRequest = null;
        _rekognition.Setup(r => r.CompareFacesAsync(It.IsAny<CompareFacesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CompareFacesRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new CompareFacesResponse
            {
                FaceMatches = new List<CompareFacesMatch> { new() { Similarity = 85f } }
            });

        using var reference = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        reference.Write(new byte[] { 9, 9, 9 });
        reference.Position = 0;

        var result = await CreateSut().CompareAsync(reference, Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeTrue();
        result.Similarity.Should().Be(85f);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.SourceImage.Bytes.ToArray().Should().Equal(new byte[] { 9, 9, 9 });
    }
}
