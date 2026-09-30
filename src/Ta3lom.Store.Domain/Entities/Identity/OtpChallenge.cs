namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `otp_challenge` (RUL-60): 6-digit code, only its HMAC is stored; 5 minutes; 5 wrong attempts per code.
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

    private OtpChallenge(Guid id, string mobile, OtpPurpose purpose, string codeHash, string? ipHash, DateTimeOffset expiresAt, DateTimeOffset now)
        : base(id)
    {
        Mobile = Guard.NotNull(mobile);
        Purpose = purpose;
        CodeHash = Guard.NotNull(codeHash);
        IpHash = ipHash;
        Attempts = 0;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    private OtpChallenge()
    {
    }

    public const int MaxAttempts = 5;

    /// The id is chosen first because it is part of the HMAC input (09 §5.7).
    public static OtpChallenge Issue(Guid id, string mobile, OtpPurpose purpose, string codeHash, string? ipHash, DateTimeOffset expiresAt, DateTimeOffset now) =>
        new(id, mobile, purpose, codeHash, ipHash, expiresAt, now);

    public string Mobile { get; private set; } = null!;

    public OtpPurpose Purpose { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public short Attempts { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? IpHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int AttemptsLeft => Math.Max(0, MaxAttempts - Attempts);

    public bool IsExhausted => Attempts >= MaxAttempts;

    public bool IsOpen(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    public void RecordFailure()
    {
        Guard.Against(ConsumedAt is not null, "otp.consumed");
        Attempts++;
    }

    public void Consume(DateTimeOffset now)
    {
        Guard.Against(ConsumedAt is not null, "otp.consumed");
        ConsumedAt = now;
    }
}
