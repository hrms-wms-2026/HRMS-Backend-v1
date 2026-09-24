using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplate;

public record GetChecklistTemplateByIdQuery(Guid Id) : IRequest<Result<ChecklistTemplateResponse>>;
