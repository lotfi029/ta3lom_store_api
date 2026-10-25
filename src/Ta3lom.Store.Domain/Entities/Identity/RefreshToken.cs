namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `refresh_token` (D-11): only the SHA-256 of the raw token is stored. Rotation keeps the family and its absolute
/// expiry (`family_expires_at`, never extended); reuse of a rotated token revokes the whole family.
public sealed class RefreshToken : AggregateRoot
{
    private RefreshToken(TokenSubjectType subjectType, Guid subjectId, string tokenHash, Guid familyId, DateTimeOffset familyExpiresAt,
        DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        SubjectType = subjectType;
        SubjectId = Guard.NotEmpty(subjectId);
        TokenHash = Guard.NotEmpty(tokenHash);
        FamilyId = Guard.NotEmpty(familyId);
        FamilyExpiresAt = familyExpiresAt;
        ExpiresAt = expiresAt <= familyExpiresAt ? expiresAt : familyExpiresAt;
        UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        CreatedAt = now;
    }

    private RefreshToken()
    {
    }

    public TokenSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    /// D-11: the absolute end of the family (60 days for donors, 8 hours for staff from the sign-in).
    public DateTimeOffset FamilyExpiresAt { get; private set; }

    /// ≤ <see cref="FamilyExpiresAt"/>.
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedBy { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// A sign-in starts a new family.
    public static RefreshToken StartFamily(TokenSubjectType subjectType, Guid subjectId, string tokenHash, TimeSpan familyLifetime,
        TimeSpan tokenLifetime, string? userAgent, DateTimeOffset now) =>
        new(subjectType, subjectId, tokenHash, Guid.CreateVersion7(), now + familyLifetime, now + tokenLifetime, userAgent, now);

    /// Rotation: the successor belongs to the same family and inherits its absolute expiry.
    public RefreshToken Rotate(string nextTokenHash, TimeSpan tokenLifetime, string? userAgent, DateTimeOffset now)
    {
        Guard.Against(IsRevoked, "refresh.already_rotated");
        var next = new RefreshToken(SubjectType, SubjectId, nextTokenHash, FamilyId, FamilyExpiresAt, now + tokenLifetime, userAgent, now);
        Revoke(now, next.Id);
        return next;
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now || FamilyExpiresAt <= now;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedBy ??= replacedBy;
    }
}
