using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Cart;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Services;
using CartEntity = Ta3lom.Store.Domain.Entities.Carts.Cart;

namespace Ta3lom.Store.Application.Features.Cart;

/// Loads the catalogue facts, the customer's ownership and the settings a cart is checked against, and renders
/// the 11 §3.1 DTO. Reused by checkout (S-15) for its re-check.
internal sealed class CartCatalog(IApplicationDbContext db, IStoreSettings settings, IClock clock)
{
    private static readonly OrderStatus[] UnpaidStatuses = [OrderStatus.PendingPayment, OrderStatus.PaymentFailed];

    private static readonly EnrolmentStatus[] OwningStatuses = [EnrolmentStatus.PendingSync, EnrolmentStatus.Active, EnrolmentStatus.Completed];

    public async Task<CartLimits> LimitsAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return new CartLimits(s.CartMaxTotal, s.MinGiftAmount, s.DedicationFreeTextEnabled,
            s.DedicationTemplates.Select(t => t.Key).ToHashSet(StringComparer.Ordinal));
    }

    /// RUL-13: courses the customer owns (live enrolment) or has in an unpaid order.
    public async Task<CustomerOwnership> OwnershipAsync(Guid? customerId, CancellationToken ct)
    {
        if (customerId is not { } id)
        {
            return CustomerOwnership.None;
        }

        var owned = await db.Enrolments.AsNoTracking().Where(e => e.CustomerId == id && OwningStatuses.Contains(e.Status))
            .Select(e => e.ProductId).ToListAsync(ct);
        var unpaid = await (from o in db.Orders.AsNoTracking()
                            join l in db.OrderLines.AsNoTracking() on o.Id equals l.OrderId
                            where o.CustomerId == id && UnpaidStatuses.Contains(o.Status) && l.Kind == LineKind.Buy && l.ProductType == ProductType.Course
                            select new { l.ProductId, o.Reference }).ToListAsync(ct);
        return new CustomerOwnership(owned.ToHashSet(),
            unpaid.GroupBy(u => u.ProductId).ToDictionary(g => g.Key, g => g.First().Reference));
    }

    /// Snapshots keyed by (product, requested format). A format of another product is treated as missing.
    public async Task<Dictionary<(Guid ProductId, Guid? FormatId), CartProduct>> SnapshotsAsync(IEnumerable<(Guid ProductId, Guid? FormatId)> keys,
        CancellationToken ct)
    {
        var wanted = keys.Distinct().ToList();
        var productIds = wanted.Select(k => k.ProductId).Distinct().ToList();
        var formatIds = wanted.Where(k => k.FormatId is not null).Select(k => k.FormatId!.Value).Distinct().ToList();
        var now = clock.UtcNow;

        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var courses = await db.CourseDetails.AsNoTracking().Where(c => productIds.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, ct);
        var causes = await db.CauseDetails.AsNoTracking().Where(c => productIds.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, ct);
        var formats = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var offers = (await db.SponsorshipOffers.AsNoTracking().Where(o => productIds.Contains(o.ProductId)).ToListAsync(ct))
            .GroupBy(o => o.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.IsActive).ThenByDescending(o => o.CreatedAt).First());
        var goals = await db.Goals.AsNoTracking().Where(g => productIds.Contains(g.ProductId)).ToDictionaryAsync(g => g.ProductId, ct);

        var result = new Dictionary<(Guid, Guid?), CartProduct>();
        foreach (var key in wanted)
        {
            if (!products.TryGetValue(key.ProductId, out var p))
            {
                continue;
            }

            goals.TryGetValue(p.Id, out var goal);
            CartFormat? format = null;
            if (key.FormatId is { } fid && formats.TryGetValue(fid, out var f) && f.ProductId == p.Id)
            {
                format = new CartFormat(f.Id, f.Kind, f.Price, f.IsActive, f.Available, f.FileAssetId is not null);
            }

            CartOffer? offer = null;
            if (offers.TryGetValue(p.Id, out var o))
            {
                offer = new CartOffer(o.Id, o.SeatPrice, o.AllowPartial, o.ZakatEligible, o.IsOpen(now, goal?.FundedUnits ?? 0), o.UnitLabelAr, o.UnitLabelEn);
            }

            CartCause? cause = null;
            if (causes.TryGetValue(p.Id, out var c))
            {
                cause = new CartCause(c.UnitAmount, c.UnitLabelAr, c.UnitLabelEn, c.AllowCustomAmount, c.MinAmount, c.ZakatEligible,
                    c.IsOpen(now, goal?.RaisedAmount ?? 0m));
            }

            result[key] = new CartProduct(p.Id, p.Type, p.Slug, p.TitleAr, p.TitleEn, p.IsPublished,
                courses.TryGetValue(p.Id, out var course) ? course.Price : null, format, offer, cause);
        }

        return result;
    }

    public async Task<CartResponse> RenderAsync(CartEntity? cart, Guid? customerId, IReadOnlyList<CartNoticeItem> notices, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (cart is null)
        {
            return new CartResponse(null, customerId is null, [], [], Summary([], s, new HashSet<Guid>()), notices, null);
        }

        var snapshots = await SnapshotsAsync(cart.Lines.Select(l => (l.ProductId, l.BookFormatId)), ct);
        var owned = await OwnershipAsync(customerId, ct);
        var now = clock.UtcNow;
        var lines = cart.Lines.OrderBy(l => l.AddedAt).Select(l => Line(l, snapshots.GetValueOrDefault((l.ProductId, l.BookFormatId)), owned, now)).ToList();
        return new CartResponse(cart.Id, cart.IsGuest, [.. lines.Where(l => l.Kind == "buy")], [.. lines.Where(l => l.Kind != "buy")],
            Summary(cart.Lines, s, snapshots.Values.Where(p => p.Format?.Kind == BookFormatKind.Print).Select(p => p.Format!.Id).ToHashSet()),
            notices, cart.ExpiresAt);
    }

    public async Task<CartBadgeResponse> BadgeAsync(CartEntity? cart, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var lines = cart?.Lines ?? [];
        var summary = Summary(lines, s, await PrintFormatsAsync(lines, ct));
        return new CartBadgeResponse(summary.LineCount, summary.GrandTotal);
    }

    /// Totals of 11 §3.1: shipping when a print line exists (RUL-26); VAT informational, shipping included (RUL-26/27).
    public static CartSummaryResponse Summary(IReadOnlyList<CartLine> lines, StoreSettings s, IReadOnlySet<Guid> printFormats)
    {
        var purchases = lines.Where(l => l.Kind == LineKind.Buy).ToList();
        var purchasesTotal = purchases.Select(l => l.Amount).Sum();
        var giftsTotal = lines.Where(l => l.IsGift).Select(l => l.Amount).Sum();
        var requiresShipping = purchases.Any(l => l.BookFormatId is { } f && printFormats.Contains(f));
        var shipping = requiresShipping ? s.ShippingFlatFee : Money.Zero;
        var rate = s.VatEnabled ? s.VatRate : 0m;                                        // same figure as the checkout quote
        var vat = purchases.Select(l => VatCalculator.Included(l.Amount, rate)).Sum() + VatCalculator.Included(shipping, rate);
        return new CartSummaryResponse(purchasesTotal.ToWire(), vat.ToWire(), shipping.ToWire(), giftsTotal.ToWire(),
            (purchasesTotal + shipping + giftsTotal).ToWire(), lines.Count, requiresShipping, lines.Any(l => l.IsGift));
    }

    private async Task<IReadOnlySet<Guid>> PrintFormatsAsync(IEnumerable<CartLine> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? new HashSet<Guid>()
            : (await db.BookFormats.AsNoTracking().Where(f => ids.Contains(f.Id) && f.Kind == BookFormatKind.Print).Select(f => f.Id).ToListAsync(ct))
                .ToHashSet();
    }

    private static CartLineResponse Line(CartLine l, CartProduct? p, CustomerOwnership owned, DateTimeOffset now)
    {
        var issues = CartLineRules.Issues(l, p, owned, now);
        var max = p is null ? l.Quantity : CartLineRules.MaxQuantity(l.Kind, p);
        string? labelAr = null, labelEn = null, seatEquivalent = null;
        if (l.Kind == LineKind.Sponsor && p?.Offer is { } o)
        {
            (labelAr, labelEn) = (o.UnitLabelAr, o.UnitLabelEn);
            if (l.Mode == LineMode.Amount && o.SeatPrice.IsPositive)
            {
                seatEquivalent = (Math.Floor(l.Amount.Amount / o.SeatPrice.Amount * 100m) / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        else if (l.Kind == LineKind.Donate && p?.Cause is { } c)
        {
            (labelAr, labelEn) = (c.UnitLabelAr, c.UnitLabelEn);
        }

        var dedication = l.Dedication is { } d
            ? new CartDedicationResponse(d.ToName, SaudiMobile.Mask(d.ToMobile), d.FromName, d.TemplateKey, d.Text)
            : null;
        return new CartLineResponse(l.Id, l.ProductId, p?.Slug ?? string.Empty, p?.Type.ToWire() ?? string.Empty, l.Kind.ToWire(), l.Mode.ToWire(),
            p?.TitleAr ?? string.Empty, p?.TitleEn, p?.Format is { } f ? new CartLineFormat(f.Id, f.Kind.ToWire()) : null, l.Quantity, max,
            l.UnitPrice.ToWire(), l.Amount.ToWire(), labelAr, labelEn, seatEquivalent, l.Intent?.ToWire(), dedication, issues);
    }
}
