using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Infrastructure.Persistence.Records;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Payments;

/// `payment` (ADR-S05): one attempt per row, `UNIQUE (order_id, attempt_no)`, `UNIQUE (provider, provider_payment_id)`.
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.ToTable("payment", t =>
        {
            t.HasCheckConstraint("ck_payment_status", "status IN ('initiated', 'captured', 'failed', 'cancelled', 'review', 'refunded')");
            t.HasCheckConstraint("ck_payment_method", "method IN ('unknown', 'mada', 'creditcard', 'applepay', 'stcpay')");
            t.HasCheckConstraint("ck_payment_amounts", "amount > 0 AND captured_amount >= 0 AND refunded_amount >= 0 AND refunded_amount <= captured_amount");
            t.HasCheckConstraint("ck_payment_attempt", "attempt_no >= 1");
            t.HasCheckConstraint("ck_payment_currency", "currency = 'SAR'");
            t.HasCheckConstraint("ck_payment_review_reason", "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment', 'external_refund', 'voided_after_capture')");
        });
        e.HasKey(x => x.Id).HasName("pk_payment");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Provider).HasColumnType("text").IsRequired();
        e.Property(x => x.ProviderPaymentId).HasColumnType("text");
        e.Property(x => x.ProviderInvoiceId).HasColumnType("text");
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.Method).HasColumnType("text").IsRequired();
        e.Property(x => x.Currency).HasColumnType("character(3)").IsRequired();
        e.Property(x => x.RedirectUrl).HasColumnType("text");
        e.Property(x => x.FailureCode).HasColumnType("text");
        e.Property(x => x.FailureMessage).HasColumnType("text");
        e.Property(x => x.ReviewReason).HasColumnType("text");
        e.Property(x => x.Version).IsRowVersion();
        e.Ignore(x => x.IsOpen);
        e.Ignore(x => x.IsCaptured);
        e.Ignore(x => x.Refundable);
        e.HasIndex(x => new { x.OrderId, x.AttemptNo }).IsUnique().HasDatabaseName("ux_payment_attempt");
        e.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique().HasFilter("provider_payment_id IS NOT NULL").HasDatabaseName("ux_payment_provider_id");
        e.HasIndex(x => new { x.Provider, x.ProviderInvoiceId }).HasFilter("provider_invoice_id IS NOT NULL").HasDatabaseName("ix_payment_provider_invoice");
        e.HasIndex(x => new { x.Status, x.InitiatedAt }).HasDatabaseName("ix_payment_status");
        e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("fk_payment_order").OnDelete(DeleteBehavior.Restrict);
    }
}

/// `payment_event`: the webhook inbox (RUL-34), append-only.
internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> e)
    {
        e.ToTable("payment_event");
        e.HasKey(x => x.Id).HasName("pk_payment_event");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Provider).HasColumnType("text").IsRequired();
        e.Property(x => x.ProviderEventId).HasColumnType("text").IsRequired();
        e.Property(x => x.Type).HasColumnType("text").IsRequired();
        e.Property(x => x.ProviderPaymentId).HasColumnType("text");
        e.Property(x => x.Outcome).HasColumnType("text");
        e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.ProviderInvoiceId).HasColumnType("text");
        e.Property(x => x.GatewayCurrency).HasColumnType("text");
        e.Property(x => x.ResolvedBy).HasColumnType("citext");
        e.Property(x => x.ResolutionNote).HasColumnType("text");
        e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique().HasDatabaseName("ux_payment_event_provider");
        e.HasIndex(x => x.Outcome).HasFilter("outcome = 'orphan'").HasDatabaseName("ix_payment_event_orphan");
        e.HasIndex(x => x.PaymentId).HasDatabaseName("ix_payment_event_payment");
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).HasConstraintName("fk_payment_event_payment").OnDelete(DeleteBehavior.Restrict);
    }
}

/// `refund` (F-D5). Σ succeeded per payment ≤ captured is enforced by the aggregates under the order row lock.
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> e)
    {
        e.ToTable("refund", t =>
        {
            t.HasCheckConstraint("ck_refund_status", "status IN ('requested', 'succeeded', 'failed')");
            t.HasCheckConstraint("ck_refund_amount", "amount > 0");
            t.HasCheckConstraint("ck_refund_source", "source IN ('store', 'gateway')");
        });
        e.HasKey(x => x.Id).HasName("pk_refund");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Amount).IsRequired();
        e.Property(x => x.Reason).HasColumnType("text").IsRequired();
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.ProviderRefundId).HasColumnType("text");
        e.Property(x => x.FailureMessage).HasColumnType("text");
        e.Property(x => x.RequestedBy).HasColumnType("citext").IsRequired();
        e.Property(x => x.IdempotencyKey).HasColumnType("text").HasMaxLength(64).IsRequired();
        // The default fills existing rows (migration); the sentinel makes EF always send the value, so no model warning.
        e.Property(x => x.Source).HasColumnType("text").IsRequired().HasDefaultValueSql("'store'").HasSentinel((Domain.Enums.RefundSource)(-1));
        // S-13: the key is the staff member's own (the Idempotency-Key filter scope is `refund:{sub}` too).
        e.HasIndex(x => new { x.OrderId, x.RequestedBy, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_refund_idempotency");
        e.HasIndex(x => x.PaymentId).HasDatabaseName("ix_refund_payment");
        e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("fk_refund_order").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).HasConstraintName("fk_refund_payment").OnDelete(DeleteBehavior.Restrict);
    }
}

/// `donation_receipt` (F-D4): gapless per (series, year); one DR per order (partial unique index).
internal sealed class DonationReceiptConfiguration : IEntityTypeConfiguration<DonationReceipt>
{
    public void Configure(EntityTypeBuilder<DonationReceipt> e)
    {
        e.ToTable("donation_receipt", t =>
        {
            t.HasCheckConstraint("ck_receipt_series", "series IN ('DR', 'DRC')");
            t.HasCheckConstraint("ck_receipt_number", "number > 0 AND year BETWEEN 2000 AND 2999");
            t.HasCheckConstraint("ck_receipt_voids", "(series = 'DRC') = (voids_receipt_id IS NOT NULL)");
        });
        e.HasKey(x => x.Id).HasName("pk_donation_receipt");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Series).HasColumnType("text").IsRequired();
        e.Property(x => x.Amount).IsRequired();
        e.Property(x => x.DonorNameSnapshot).HasColumnType("text").IsRequired();
        e.Property(x => x.LinesSnapshot).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.PdfStorageKey).HasColumnType("text");
        e.Ignore(x => x.DisplayNumber);
        e.HasIndex(x => new { x.Series, x.Year, x.Number }).IsUnique().HasDatabaseName("ux_receipt_number");
        e.HasIndex(x => x.OrderId).IsUnique().HasFilter("series = 'DR'").HasDatabaseName("ux_receipt_order_dr");
        e.HasIndex(x => x.OrderId).HasDatabaseName("ix_receipt_order");
        e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("fk_receipt_order").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<DonationReceipt>().WithMany().HasForeignKey(x => x.VoidsReceiptId).HasConstraintName("fk_receipt_voids").OnDelete(DeleteBehavior.Restrict);
    }
}

/// `document_counter`: next number per (series, year); touched only by `DocumentNumbers` (upsert under a row lock).
internal sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounterRecord>
{
    public void Configure(EntityTypeBuilder<DocumentCounterRecord> e)
    {
        e.ToTable("document_counter", t => t.HasCheckConstraint("ck_document_counter_next", "next_number >= 1"));
        e.HasKey(x => new { x.Series, x.Year }).HasName("pk_document_counter");
        e.Property(x => x.Series).HasColumnType("text");
        e.Property(x => x.NextNumber).IsRequired();
    }
}
