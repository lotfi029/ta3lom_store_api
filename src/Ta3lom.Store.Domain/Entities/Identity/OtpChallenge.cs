namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `otp_challenge` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class OtpChallenge : AggregateRoot
{
    public OtpChallenge(string mobile, OtpPurpose purpose, string codeHash, DateTimeOffset expiresAt, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Mobile = Guard.NotNull(mobile);
        Purpose = purpose;
        CodeHash = Guard.NotNull(codeHash);
        Attempts = 0;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    private OtpChallenge()
    {
    }

    public string Mobile { get; private set; } = null!;

    public OtpPurpose Purpose { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public short Attempts { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? IpHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
