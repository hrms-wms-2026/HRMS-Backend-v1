using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceMatchService : IFaceMatchService
{
    private readonly IAmazonRekognition _rekognition;
    private readonly AwsRekognitionOptions _options;

    public RekognitionFaceMatchService(IAmazonRekognition rekognition, IOptions<AwsRekognitionOptions> options)
    {
        _rekognition = rekognition;
        _options = options.Value;
    }

    public async Task<FaceMatchOutcome> CompareAsync(Stream referenceImage, Stream capturedImage, CancellationToken ct)
    {
        var sourceBytes = await ToMemoryStreamAsync(referenceImage, ct);
        var targetBytes = await ToMemoryStreamAsync(capturedImage, ct);

        var response = await _rekognition.CompareFacesAsync(new CompareFacesRequest
        {
            SourceImage = new Image { Bytes = sourceBytes },
            TargetImage = new Image { Bytes = targetBytes },
            SimilarityThreshold = _options.FaceMatchSimilarityThreshold
        }, ct);

        var bestMatch = response.FaceMatches?
            .OrderByDescending(m => m.Similarity)
            .FirstOrDefault();

        return bestMatch is null
            ? new FaceMatchOutcome(false, 0f)
            : new FaceMatchOutcome(true, bestMatch.Similarity ?? 0f);
    }

    private static async Task<MemoryStream> ToMemoryStreamAsync(Stream input, CancellationToken ct)
    {
        var copy = new MemoryStream();
        await input.CopyToAsync(copy, ct);
        copy.Position = 0;
        return copy;
    }
}
