namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `refresh_token` (09 §5.8): only the SHA-256 of the raw token is stored; rotation keeps the family,
/// reuse of a rotated token revokes the whole family (RUL-60, FR-S28).
public sealed class RefreshToken : AggregateRoot
{
    public RefreshToken(TokenSubjectType subjectType, Guid subjectId, string tokenHash, Guid familyId, DateTimeOffset expiresAt, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        SubjectType = subjectType;
        SubjectId = Guard.NotEmpty(subjectId);
        TokenHash = Guard.NotNull(tokenHash);
        FamilyId = Guard.NotEmpty(familyId);
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    private RefreshToken()
    {
    }

    public static RefreshToken Issue(TokenSubjectType subjectType, Guid subjectId, string tokenHash, Guid familyId, DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now) =>
        new(subjectType, subjectId, tokenHash, familyId, expiresAt, now) { UserAgent = Truncate(userAgent) };

    public TokenSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedBy ??= replacedBy;
    }

    private static string? Truncate(string? userAgent) => userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
}
