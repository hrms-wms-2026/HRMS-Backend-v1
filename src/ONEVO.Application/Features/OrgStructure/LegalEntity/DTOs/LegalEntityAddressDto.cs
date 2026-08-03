namespace ONEVO.Application.Features.OrgStructure.DTOs;

// Registered business address shape. No OneVo-HR doc or existing codebase
// convention defines this shape (Part 1 audit confirmed no AddressDto exists
// anywhere), so this mirrors the Part 1 draft response shape verbatim and is
// the one place this shape is defined - both the request and response reuse
// this same type rather than duplicating fields.
public record LegalEntityAddressDto(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);
