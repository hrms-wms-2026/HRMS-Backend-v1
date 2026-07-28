using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Features.IdentityVerification.Services;

namespace ONEVO.Infrastructure.ExternalServices.IdentityVerification;

public sealed class RekognitionFaceComparisonService
    : IFaceComparisonService, IDisposable
{
    private readonly AmazonRekognitionClient? _client;

    public RekognitionFaceComparisonService(IConfiguration configuration)
    {
        var regionName =
            configuration["IdentityVerification:AwsRegion"] ??
            configuration["AWS_REGION"];
        if (!string.IsNullOrWhiteSpace(regionName))
        {
            _client = new AmazonRekognitionClient(
                RegionEndpoint.GetBySystemName(regionName.Trim()));
        }
    }

    public async Task<FaceDetectionResult> DetectFacesAsync(
        byte[] imageBytes,
        CancellationToken ct)
    {
        if (_client is null)
        {
            return new FaceDetectionResult(
                false,
                0,
                "face_provider_region_not_configured");
        }

        try
        {
            using var bytes = new MemoryStream(
                imageBytes,
                writable: false);
            var response = await _client.DetectFacesAsync(
                new DetectFacesRequest
                {
                    Image = new Image { Bytes = bytes },
                    Attributes = ["ALL"]
                },
                ct);
            return new FaceDetectionResult(
                true,
                response.FaceDetails?.Count ?? 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonServiceException)
        {
            return new FaceDetectionResult(
                false,
                0,
                "face_provider_unavailable");
        }
        catch (HttpRequestException)
        {
            return new FaceDetectionResult(
                false,
                0,
                "face_provider_unavailable");
        }
    }

    public async Task<FaceComparisonResult> CompareFacesAsync(
        byte[] referenceImageBytes,
        byte[] candidateImageBytes,
        decimal threshold,
        CancellationToken ct)
    {
        if (_client is null)
        {
            return new FaceComparisonResult(
                false,
                0,
                "face_provider_region_not_configured");
        }

        try
        {
            using var source = new MemoryStream(
                referenceImageBytes,
                writable: false);
            using var target = new MemoryStream(
                candidateImageBytes,
                writable: false);
            var response = await _client.CompareFacesAsync(
                new CompareFacesRequest
                {
                    SourceImage = new Image { Bytes = source },
                    TargetImage = new Image { Bytes = target },
                    SimilarityThreshold = (float)Math.Clamp(
                        threshold,
                        0m,
                        100m)
                },
                ct);
            var similarity =
                response.FaceMatches is { Count: > 0 }
                    ? response.FaceMatches.Max(match => match.Similarity)
                    : 0f;
            return new FaceComparisonResult(
                true,
                (decimal)similarity);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonServiceException)
        {
            return new FaceComparisonResult(
                false,
                0,
                "face_provider_unavailable");
        }
        catch (HttpRequestException)
        {
            return new FaceComparisonResult(
                false,
                0,
                "face_provider_unavailable");
        }
    }

    public void Dispose() => _client?.Dispose();
}

