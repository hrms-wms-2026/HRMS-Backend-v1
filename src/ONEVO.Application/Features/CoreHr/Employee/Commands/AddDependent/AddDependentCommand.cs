using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public record AddDependentCommand(string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone)
    : IRequest<Result<Guid>>;
