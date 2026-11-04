using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;

public sealed record UploadObjectiveAssetCommand(
    Guid ObjectiveId, string OriginalFileName, string ContentType, Stream Content
) : IRequest<Result<ObjectiveAssetResponse>>;
