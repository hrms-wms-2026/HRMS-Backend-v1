namespace ONEVO.Api.Contracts.Auth;

public record ForcePasswordChangeRequest(string Email, string CurrentPassword, string NewPassword);
