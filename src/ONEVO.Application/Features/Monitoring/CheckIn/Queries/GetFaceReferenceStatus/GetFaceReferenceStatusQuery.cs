namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetFaceReferenceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

/// <summary>
/// Whether the tray device's employee already has an enrolled face. Tray device setup skips
/// face setup when they do — clock-in still verifies every time against that face.
/// </summary>
public sealed record GetFaceReferenceStatusQuery : IRequest<Result<FaceReferenceStatusDto>>;
