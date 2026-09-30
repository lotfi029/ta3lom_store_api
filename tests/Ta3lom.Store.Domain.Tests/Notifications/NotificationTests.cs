using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Notifications;

/// Sent notifications, the outbox and editable templates (RUL-63, FR-S77).
public sealed class NotificationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Rule("RUL-63")]
    public void The_otp_sms_is_a_direct_send_outside_the_outbox()
    {
        var n = Notification.Direct(MessageChannel.Sms, "+966551234567", "otp_login", "ar", null, T0);

        n.Channel.ShouldBe(MessageChannel.Sms);
        n.Recipient.ShouldBe("+966551234567");
        n.TemplateKey.ShouldBe("otp_login");
        n.Locale.ShouldBe("ar");
        n.Status.ShouldBe(NotificationStatus.Queued);
        n.OutboxMessageId.ShouldBeNull();
        n.RelatedOrderId.ShouldBeNull();
        n.CreatedAt.ShouldBe(T0);
        n.SentAt.ShouldBeNull();
        Should.Throw<DomainException>(() => new Notification(MessageChannel.Email, null!, "k", "ar", T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-63")]
    public void An_outbox_send_is_linked_to_its_message_and_order()
    {
        var message = Guid.CreateVersion7();
        var order = Guid.CreateVersion7();

        var n = Notification.ForOutbox(message, MessageChannel.Email, "n@example.com", "order_paid", "en", order, T0);

        n.OutboxMessageId.ShouldBe(message);
        n.RelatedOrderId.ShouldBe(order);
    }

    [Fact]
    public void Sent_clears_a_previous_error()
    {
        var n = Notification.Direct(MessageChannel.Sms, "+966551234567", "otp_login", "ar", null, T0);
        n.MarkFailed("timeout", T0);

        n.MarkSent("msg-1", T0.AddSeconds(5));

        n.Status.ShouldBe(NotificationStatus.Sent);
        n.ProviderMsgId.ShouldBe("msg-1");
        n.Error.ShouldBeNull();
        n.SentAt.ShouldBe(T0.AddSeconds(5));
    }

    [Fact]
    public void Failure_text_is_capped_at_500_characters()
    {
        var shortFailure = Notification.Direct(MessageChannel.Sms, "+966551234567", "k", "ar", null, T0);
        var longFailure = Notification.Direct(MessageChannel.Sms, "+966551234567", "k", "ar", null, T0);

        shortFailure.MarkFailed("provider down", T0);
        longFailure.MarkFailed(new string('e', 800), T0);

        shortFailure.Status.ShouldBe(NotificationStatus.Failed);
        shortFailure.Error.ShouldBe("provider down");
        longFailure.Error!.Length.ShouldBe(500);
        longFailure.SentAt.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-63")]
    public void An_outbox_message_is_due_at_once()
    {
        var m = new OutboxMessage("OrderPaid", """{"orderId":"x"}""", T0);

        m.Type.ShouldBe("OrderPaid");
        m.Payload.ShouldBe("""{"orderId":"x"}""");
        m.OccurredAt.ShouldBe(T0);
        m.NextAttemptAt.ShouldBe(T0);
        m.Attempts.ShouldBe(0);
        m.ProcessedAt.ShouldBeNull();
        m.LastError.ShouldBeNull();
        Should.Throw<DomainException>(() => new OutboxMessage("t", null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void Template_edit_drops_a_blank_subject()
    {
        var t = new MessageTemplate("order_paid", MessageChannel.Email, "ar", "مرحبا {{ name }}", T0);

        t.Key.ShouldBe("order_paid");
        t.Channel.ShouldBe(MessageChannel.Email);
        t.Locale.ShouldBe("ar");
        t.Body.ShouldBe("مرحبا {{ name }}");
        t.Subject.ShouldBeNull();
        t.UpdatedAt.ShouldBe(T0);

        t.Edit("تم الدفع", "شكرا {{ name }}", T0.AddDays(1));
        t.Subject.ShouldBe("تم الدفع");
        t.Body.ShouldBe("شكرا {{ name }}");
        t.UpdatedAt.ShouldBe(T0.AddDays(1));

        t.Edit("  ", "نص", T0.AddDays(2));
        t.Subject.ShouldBeNull();
        Should.Throw<DomainException>(() => t.Edit(null, null!, T0)).Code.ShouldBe("guard.null");
        Should.Throw<DomainException>(() => new MessageTemplate(null!, MessageChannel.Sms, "ar", "b", T0)).Code.ShouldBe("guard.null");
    }
}
