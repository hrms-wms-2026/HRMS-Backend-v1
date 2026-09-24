namespace ONEVO.Api.Contracts.CoreHr.Employees;

public record UpdatePersonalInformationRequest(
    string FirstName,
    string LastName,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    Guid? NationalityId,
    string? DisplayTimezone,
    IReadOnlyList<UpdateAddressRequest> Addresses,
    string Version);

public record UpdateAddressRequest(string AddressType, string AddressJson, bool IsPrimary);
