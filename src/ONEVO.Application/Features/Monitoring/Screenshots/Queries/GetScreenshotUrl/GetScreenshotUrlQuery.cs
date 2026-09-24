using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshotUrl;

public record GetScreenshotUrlQuery(Guid EvidenceAssetId) : IRequest<Result<ScreenshotUrlDto>>;
