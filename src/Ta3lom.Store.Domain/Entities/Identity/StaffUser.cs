namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `staff_user` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
