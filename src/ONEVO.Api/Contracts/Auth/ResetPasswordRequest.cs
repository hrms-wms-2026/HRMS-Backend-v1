namespace ONEVO.Api.Contracts.Auth;

public record ResetPasswordRequest(string Token, string NewPassword);
