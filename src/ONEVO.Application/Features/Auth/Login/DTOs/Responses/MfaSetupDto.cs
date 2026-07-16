using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

public record MfaSetupDto(string Secret, string QrCodeUri);
