namespace Fiore.Api.Domain.Identity;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public UserAccountStatus Status { get; set; } = UserAccountStatus.Active;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<UserRole> Roles { get; set; } = [];
}

public sealed class AppRole
{
    public short Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<UserRole> Users { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public short RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
}
