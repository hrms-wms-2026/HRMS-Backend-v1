using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.DTOs;

namespace ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Queries.ListPositionTemplatePacks;

public sealed record ListPositionTemplatePacksQuery : IRequest<Result<PositionTemplatePackListResponseDto>>;
