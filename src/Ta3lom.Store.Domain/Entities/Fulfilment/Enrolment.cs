namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `enrolment` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
