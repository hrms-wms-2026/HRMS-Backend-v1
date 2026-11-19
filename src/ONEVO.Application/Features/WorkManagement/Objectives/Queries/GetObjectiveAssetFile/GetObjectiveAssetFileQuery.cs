using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveAssetFile;

public sealed record GetObjectiveAssetFileQuery(Guid ObjectiveId, Guid AssetId) : IRequest<Result<ObjectiveAssetFile>>;

public sealed record ObjectiveAssetFile(Stream Content, string ContentType, string FileName);
