using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.ArchiveChecklistTemplate;

public record ArchiveChecklistTemplateCommand(Guid Id) : IRequest<Result>;
