using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public record UpdateAddressInput(string AddressType, string AddressJson, bool IsPrimary);

public record UpdatePersonalInformationCommand(
    string FirstName,
    string LastName,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    Guid? NationalityId,
    string? DisplayTimezone,
    IReadOnlyList<UpdateAddressInput> Addresses,
    string Version) : IRequest<Result>;
