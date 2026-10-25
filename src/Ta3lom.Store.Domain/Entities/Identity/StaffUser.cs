namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `staff_user` (BR-A11, D-12): email + password + mandatory TOTP and a roles array. Five failures (password or TOTP)
/// within 15 minutes lock the account for one hour. A new or reset account must change its one-time password first.
public sealed class StaffUser : AggregateRoot
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(1);

    private StaffUser(string email, string name, string passHash, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Email = email;
        Name = name;
        PassHash = passHash;
        MustChangePassword = true;
        Roles = [];
        IsActive = true;
        CreatedAt = now;
    }

    private StaffUser()
    {
    }

    public string Email { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string PassHash { get; private set; } = null!;

    /// True after create and after an admin reset: only the password change and logout are allowed until it is done.
    public bool MustChangePassword { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    /// Data Protection ciphertext of the TOTP secret.
    public byte[]? TotpSecretEnc { get; private set; }

    /// The last accepted TOTP time step: a code of this step or an earlier one is never accepted again (replay guard).
    public long? TotpLastStep { get; private set; }

    public string[] Roles { get; private set; } = null!;

    public int FailedCount { get; private set; }

    /// Start of the current 15-minute failure window.
    public DateTimeOffset? FirstFailedAt { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool TotpEnrolled => TotpSecretEnc is not null;

    /// The authenticator has been confirmed by a first successful code; re-enrolling is allowed until then.
    public bool TotpVerified => TotpSecretEnc is not null && TotpLastStep is not null;

    public bool IsAdmin => HasRole(Constants.Roles.Admin);

    public static StaffUser Create(string email, string name, string passHash, IEnumerable<string> roles, DateTimeOffset now)
    {
        var staff = new StaffUser(Guard.NotEmpty(email).Trim(), Guard.NotEmpty(name).Trim(), Guard.NotEmpty(passHash), now);
        staff.SetRoles(roles);
        return staff;
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public bool HasRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    /// A wrong password or TOTP code (D-12). Returns true when this failure locked the account.
    public bool RecordFailure(DateTimeOffset now)
    {
        if (FirstFailedAt is not { } first || now - first > FailureWindow)
        {
            FirstFailedAt = now;
            FailedCount = 0;
        }

        FailedCount++;
        if (FailedCount < MaxFailures)
        {
            return false;
        }

        LockedUntil = now + LockoutDuration;
        FailedCount = 0;
        FirstFailedAt = null;
        return true;
    }

    /// Password and TOTP both passed.
    public void RecordLogin(DateTimeOffset now)
    {
        FailedCount = 0;
        FirstFailedAt = null;
        LockedUntil = null;
        LastLoginAt = now;
    }

    /// Stores a new authenticator secret; allowed until the current one has been verified.
    public void EnrolTotp(byte[] protectedSecret)
    {
        Guard.Against(protectedSecret is null || protectedSecret.Length == 0, "staff.totp_secret_required");
        Guard.Against(TotpVerified, "staff.totp_already_verified");
        TotpSecretEnc = protectedSecret;
        TotpLastStep = null;
    }

    /// Accepts a verified code's time step once (D-12). False when the step was already used.
    public bool AcceptTotpStep(long step)
    {
        if (TotpLastStep is { } last && step <= last)
        {
            return false;
        }

        TotpLastStep = step;
        return true;
    }

    /// Admin action: the next sign-in enrols a new authenticator.
    public void ResetTotp()
    {
        TotpSecretEnc = null;
        TotpLastStep = null;
    }

    /// The member changed their own password: the forced change is done.
    public void ChangePassword(string passHash, DateTimeOffset now)
    {
        PassHash = Guard.NotEmpty(passHash);
        MustChangePassword = false;
        PasswordChangedAt = now;
    }

    /// Admin reset: a one-time password that must be changed at the next sign-in.
    public void ResetPassword(string oneTimePassHash, DateTimeOffset now)
    {
        PassHash = Guard.NotEmpty(oneTimePassHash);
        MustChangePassword = true;
        PasswordChangedAt = now;
    }

    /// The stored hash was made with older parameters: replace it with the current ones (same password).
    public void UpgradePasswordHash(string passHash) => PassHash = Guard.NotEmpty(passHash);

    public void Unlock()
    {
        FailedCount = 0;
        FirstFailedAt = null;
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
