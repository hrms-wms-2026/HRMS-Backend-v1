using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;

public record AddEmergencyContactCommand(string Name, string Relationship, string Phone, string? Email, bool IsPrimary)
    : IRequest<Result<Guid>>;
