using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;

public record DeleteDependentCommand(Guid DependentId) : IRequest<Result>;
