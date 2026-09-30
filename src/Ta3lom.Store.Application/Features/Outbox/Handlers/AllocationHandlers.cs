using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// RUL-52: SMS `beneficiary_invite` (course title + "My courses" link) to the beneficiary's or guardian's mobile, once. The
/// mobile is decrypted only here and stored masked in `notification.recipient` (PDPL).
internal sealed class BeneficiaryInviteHandler(IApplicationDbContext db, NotificationDispatch dispatch, IWebLinks links) : OutboxHandler<BeneficiaryInvite>
{
    public const string Locale = "ar";

    protected override async Task HandleAsync(BeneficiaryInvite message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var beneficiary = await db.Beneficiaries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == message.BeneficiaryId, ct);
        var course = await (from e in db.Enrolments.AsNoTracking()
                            join p in db.Products.AsNoTracking() on e.ProductId equals p.Id
                            where e.Id == message.EnrolmentId
                            select new { p.TitleAr, e.Status }).FirstOrDefaultAsync(ct);
        if (beneficiary?.Mobile is null || course is null || course.Status == EnrolmentStatus.Revoked)
        {
            return;                                                                           // no mobile any more, or the seat is gone
        }

        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, beneficiary.Mobile, "beneficiary_invite", Locale,
            new { product = course.TitleAr, link = links.MyCourses(Locale) }, null, ct, storedRecipient: SaudiMobile.Mask(beneficiary.Mobile));
    }
}

/// RUL-50: short SMS `beneficiary_lapsed` to the former beneficiary of a lapsed seat, once (mobile stored masked).
internal sealed class UnitLapsedHandler(IApplicationDbContext db, NotificationDispatch dispatch) : OutboxHandler<UnitLapsed>
{
    protected override async Task HandleAsync(UnitLapsed message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var beneficiary = await db.Beneficiaries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == message.BeneficiaryId, ct);
        var title = await db.Products.AsNoTracking().Where(p => p.Id == message.ProductId).Select(p => p.TitleAr).FirstAsync(ct);
        if (beneficiary?.Mobile is null)
        {
            return;
        }

        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, beneficiary.Mobile, "beneficiary_lapsed", BeneficiaryInviteHandler.Locale,
            new { product = title }, null, ct, storedRecipient: SaudiMobile.Mask(beneficiary.Mobile));
    }
}

/// RUL-49: staff e-mail `staff_alert` for units waiting past `allocation_alert_days`, once per message.
internal sealed class AllocationAlertHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<AllocationAlert>
{
    protected override Task HandleAsync(AllocationAlert message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            AllocationMail.Model("AllocationAlert", message.Slug, message.Units, message.OldestFundedAt, message.Day), null, ct);
}

/// RUL-49: the manager's e-mail for units waiting past `allocation_escalate_days`, once per message.
internal sealed class AllocationEscalationHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<AllocationEscalation>
{
    protected override Task HandleAsync(AllocationEscalation message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.ManagerAlertsAddress, "staff_alert", "ar",
            AllocationMail.Model("AllocationEscalation", message.Slug, message.Units, message.OldestFundedAt, message.Day), null, ct);
}

internal static class AllocationMail
{
    public static object Model(string kind, string slug, int units, DateTimeOffset oldest, DateOnly day) => new
    {
        subject = string.Create(CultureInfo.InvariantCulture, $"{kind} {slug}: {units}"),
        body = string.Create(CultureInfo.InvariantCulture,
            $"وحدات بانتظار التخصيص: {units}\nproduct: {slug}\noldest funded: {oldest:yyyy-MM-dd}\nday: {day:yyyy-MM-dd}"),
    };
}

/// RUL-50 digest feed: nothing is sent per unit; `SponsorDigest` (S-45) reads the unit history monthly.
internal sealed class SponsorUnitAllocatedHandler : OutboxHandler<SponsorUnitAllocated>
{
    protected override Task HandleAsync(SponsorUnitAllocated message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class SponsorUnitCompletedHandler : OutboxHandler<SponsorUnitCompleted>
{
    protected override Task HandleAsync(SponsorUnitCompleted message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}
