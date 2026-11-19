using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Services;

public class EnrolledFaceMatcher : IEnrolledFaceMatcher
{
    private readonly IFileStorageService _fileStorage;
    private readonly IFaceMatchService _faceMatch;

    public EnrolledFaceMatcher(IFileStorageService fileStorage, IFaceMatchService faceMatch)
    {
        _fileStorage = fileStorage;
        _faceMatch = faceMatch;
    }

    public async Task<EnrolledFaceMatch> MatchAsync(
        Guid tenantId, BiometricProfile? profile, Stream captured, CancellationToken ct)
    {
        var references = ReferenceIds(profile);
        if (references.Count == 0)
            return new EnrolledFaceMatch(HasReference: false, IsMatch: false, Similarity: null, Failed: false);

        // Each CompareFaces call reads the capture from position 0.
        using var capturedCopy = new MemoryStream();
        if (captured.CanSeek)
            captured.Position = 0;
        await captured.CopyToAsync(capturedCopy, ct);

        float? best = null;
        var compared = 0;
        foreach (var referenceId in references)
        {
            try
            {
                var read = await _fileStorage.OpenReadAsync(tenantId, referenceId, ct);
                if (!read.IsSuccess)
                    continue;

                await using var referenceStream = read.Value!.Content;
                capturedCopy.Position = 0;
                var outcome = await _faceMatch.CompareAsync(referenceStream, capturedCopy, ct);
                compared++;
                best = best is null ? outcome.Similarity : Math.Max(best.Value, outcome.Similarity);

                if (outcome.IsMatch)
                    return new EnrolledFaceMatch(true, true, outcome.Similarity, Failed: false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // One unreadable reference must not hide a match against another.
            }
        }

        return new EnrolledFaceMatch(true, IsMatch: false, best, Failed: compared == 0);
    }

    private static List<Guid> ReferenceIds(BiometricProfile? profile)
    {
        var ids = new List<Guid>(3);
        if (profile?.ReferencePhotoFileId is { } front) ids.Add(front);
        if (profile?.LeftReferencePhotoFileId is { } left) ids.Add(left);
        if (profile?.RightReferencePhotoFileId is { } right) ids.Add(right);
        return ids;
    }
}
