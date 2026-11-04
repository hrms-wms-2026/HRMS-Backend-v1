using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;

public sealed record DeleteObjectiveAssetCommand(Guid ObjectiveId, Guid AssetId) : IRequest<Result>;
