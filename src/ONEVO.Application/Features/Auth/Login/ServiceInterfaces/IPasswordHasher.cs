namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
