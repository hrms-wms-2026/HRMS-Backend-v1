namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public interface ITotpService
{
    bool Verify(string base32Secret, string code);
}
