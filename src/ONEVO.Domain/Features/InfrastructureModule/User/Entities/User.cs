using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.InfrastructureModule.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public bool MustChangePassword { get; set; } = false;
    public bool PasswordSetByAdmin { get; set; } = false;
    public DateTimeOffset? TemporaryPasswordExpiresAt { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
