namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `refresh_token` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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

    public TokenSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? UserAgent { get; private set; }
}
