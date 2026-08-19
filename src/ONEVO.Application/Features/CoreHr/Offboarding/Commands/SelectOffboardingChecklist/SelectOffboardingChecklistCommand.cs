using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;

public sealed record SelectOffboardingChecklistCommand(Guid EmployeeId, Guid TemplateId) : IRequest<Result>;
