using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public record UpdateEmergencyContactCommand(Guid ContactId, string Name, string Relationship, string Phone, string? Email, bool IsPrimary)
    : IRequest<Result>;
