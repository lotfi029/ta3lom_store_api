namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `enrolment` (RUL-42): one live enrolment per customer and course (`ux_enrolment_live`); LMS access is granted from the outbox.
public sealed class Enrolment : AggregateRoot
{
    public Enrolment(Guid customerId, Guid productId, EnrolmentSource source, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        CustomerId = Guard.NotEmpty(customerId);
        ProductId = Guard.NotEmpty(productId);
        Source = source;
        Status = EnrolmentStatus.Active;
        ProgressPct = 0;
        GrantedAt = now;
    }

    private Enrolment()
    {
    }

    public Guid CustomerId { get; private set; }

    public Guid ProductId { get; private set; }

    public EnrolmentSource Source { get; private set; }

    public Guid? OrderLineId { get; private set; }

    public Guid? SponsoredUnitId { get; private set; }

    public EnrolmentStatus Status { get; private set; }

    public short ProgressPct { get; private set; }

    public string? LmsEnrolmentRef { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CertificateNo { get; private set; }

    public bool IsLive => Status is EnrolmentStatus.PendingSync or EnrolmentStatus.Active or EnrolmentStatus.Completed;

    /// RUL-47/52: a sponsored seat for the beneficiary's customer row; `pending_sync` until the LMS accepts it.
    public static Enrolment ForSponsoredUnit(Guid customerId, Guid productId, Guid sponsoredUnitId, int? accessDays, DateTimeOffset now) =>
        new(customerId, productId, EnrolmentSource.Sponsored, now)
        {
            SponsoredUnitId = Guard.NotEmpty(sponsoredUnitId),
            Status = EnrolmentStatus.PendingSync,
            ExpiresAt = accessDays is > 0 ? now.AddDays(accessDays.Value) : null,
        };

    /// RUL-39/42: a purchase starts `pending_sync`; the `LmsEnrol` outbox message grants access and makes it `active`.
    /// Access is lifetime unless the course defines `access_days`.
    public static Enrolment ForPurchase(Guid customerId, Guid productId, Guid orderLineId, int? accessDays, DateTimeOffset now) =>
        new(customerId, productId, EnrolmentSource.Purchase, now)
        {
            OrderLineId = Guard.NotEmpty(orderLineId),
            Status = EnrolmentStatus.PendingSync,
            ExpiresAt = accessDays is > 0 ? now.AddDays(accessDays.Value) : null,
        };

    /// The LMS accepted the enrolment (RUL-42). Idempotent.
    public void MarkSynced(string lmsEnrolmentRef)
    {
        LmsEnrolmentRef = lmsEnrolmentRef;
        if (Status == EnrolmentStatus.PendingSync)
        {
            Status = EnrolmentStatus.Active;
        }
    }

    /// FR-S23: launchable while active or completed and not past `expires_at`.
    public bool CanLaunch(DateTimeOffset now) =>
        Status is EnrolmentStatus.Active or EnrolmentStatus.Completed && (ExpiresAt is null || ExpiresAt > now);

    /// First launch starts the course (RUL-50 lapse uses `started_at`).
    public void RecordLaunch(DateTimeOffset now) => StartedAt ??= now;

    /// RUL-42/50 (S-40): progress from the LMS. Idempotent: the percentage never goes down, `started_at`, `completed_at` and
    /// `certificate_no` are set once. Returns true only on the call that completes the course.
    public bool ApplyProgress(short progressPct, DateTimeOffset? startedAt, DateTimeOffset? completedAt, string? certificateNo)
    {
        if (Status is not (EnrolmentStatus.Active or EnrolmentStatus.Completed))
        {
            return false;
        }

        ProgressPct = Math.Max(ProgressPct, Math.Clamp(progressPct, (short)0, (short)100));
        StartedAt ??= startedAt;
        if (!string.IsNullOrWhiteSpace(certificateNo))
        {
            CertificateNo ??= certificateNo.Trim();
        }

        if (completedAt is null || Status == EnrolmentStatus.Completed)
        {
            return false;
        }

        Status = EnrolmentStatus.Completed;
        CompletedAt = completedAt;
        StartedAt ??= completedAt;
        ProgressPct = 100;
        return true;
    }

    /// RUL-50 (lapse), RUL-58 (refund): access withdrawn; the `LmsRevoke` message tells the LMS.
    public void Revoke()
    {
        if (IsLive)
        {
            Status = EnrolmentStatus.Revoked;
        }
    }
}
