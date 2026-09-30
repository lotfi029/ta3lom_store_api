namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `staff_user` (RUL-62): email + password + mandatory TOTP, roles array, lockout after 5 failures
/// (password or TOTP) for one hour (main-site FR-53 rule, 09 §5.8).
public sealed class StaffUser : AggregateRoot
{
    public StaffUser(string email, string name, string passHash, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Email = Guard.NotNull(email);
        Name = Guard.NotNull(name);
        PassHash = Guard.NotNull(passHash);
        Roles = ["viewer"];
        FailedCount = 0;
        IsActive = true;
        CreatedAt = now;
    }

    private StaffUser()
    {
    }

    public const int MaxFailures = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(1);

    public static StaffUser Create(string email, string name, string passHash, IEnumerable<string> roles, DateTimeOffset now)
    {
        var staff = new StaffUser(email.Trim(), name.Trim(), passHash, now);
        staff.SetRoles(roles);
        return staff;
    }

    public string Email { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string PassHash { get; private set; } = null!;

    public byte[]? TotpSecretEnc { get; private set; }

    public string[] Roles { get; private set; } = null!;

    public int FailedCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool TotpEnrolled => TotpSecretEnc is not null;

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public bool HasRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    /// A wrong password or TOTP code. The fifth consecutive failure locks the account for one hour.
    public void RecordFailure(DateTimeOffset now)
    {
        FailedCount++;
        if (FailedCount >= MaxFailures)
        {
            LockedUntil = now + LockoutDuration;
            FailedCount = 0;
        }
    }

    /// Password and TOTP both passed.
    public void RecordLogin(DateTimeOffset now)
    {
        FailedCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    public void EnrolTotp(byte[] protectedSecret)
    {
        Guard.Against(protectedSecret is null || protectedSecret.Length == 0, "staff.totp_secret_required");
        TotpSecretEnc = protectedSecret;
    }

    /// Admin action: the next login must enrol a new authenticator.
    public void ResetTotp() => TotpSecretEnc = null;

    public void ChangePasswordHash(string passHash) => PassHash = Guard.NotNull(passHash);

    public void Unlock()
    {
        FailedCount = 0;
        LockedUntil = null;
    }

    public void Update(string name, IEnumerable<string> roles, bool isActive)
    {
        Name = Guard.NotEmpty(name).Trim();
        SetRoles(roles);
        IsActive = isActive;
    }

    private void SetRoles(IEnumerable<string> roles)
    {
        string[] distinct = [.. roles.Distinct(StringComparer.Ordinal)];
        Guard.Against(distinct.Length == 0, "staff.roles_required");
        Guard.Against(distinct.Any(r => !Constants.Roles.IsValid(r)), "staff.role_invalid");
        Roles = distinct;
    }
}
