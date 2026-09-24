using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record AcceptEmployeeInvitationRequest(
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("confirm_password")] string ConfirmPassword,
    [property: JsonPropertyName("acceptances")] IReadOnlyList<LegalAcceptanceItemRequest> Acceptances);
