using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public record UpdateDependentCommand(Guid DependentId, string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone)
    : IRequest<Result>;
