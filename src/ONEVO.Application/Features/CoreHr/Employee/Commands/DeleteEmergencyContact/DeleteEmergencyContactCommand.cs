using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;

public record DeleteEmergencyContactCommand(Guid ContactId) : IRequest<Result>;
