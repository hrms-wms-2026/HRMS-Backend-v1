using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class UserMfa : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string MethodType { get; set; } = string.Empty; // "totp" | "email_otp" | "recovery_code"
    public string Secret { get; set; } = string.Empty;     // Encrypted TOTP secret or hashed recovery code
    public bool IsVerified { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
