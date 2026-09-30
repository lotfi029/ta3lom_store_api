using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Domain.Entities.Carts;

/// The RUL-12 matrix, RUL-19 intent and RUL-21 dedication checks, shared by the cart and the checkout re-check (S-15).
public static class CartLineRules
{
    public const int GiftMaxQuantity = 999;

    /// Line maximum: course and digital 1, print 20, gifts 999 (RUL-12).
    public static int MaxQuantity(LineKind kind, CartProduct p) => kind switch
    {
        LineKind.Buy when p.Type == ProductType.Course => 1,
        LineKind.Buy when p.Format?.Kind == BookFormatKind.Digital => 1,
        LineKind.Buy => CatalogLimits.PrintMaxQuantity,
        _ => GiftMaxQuantity,
    };

    /// RUL-12 quantity rule for single-seat purchases: course and digital quantities are forced to 1.
    public static int NormaliseQuantity(CartLineSpec s, CartProduct p) =>
        s.Kind == LineKind.Buy && MaxQuantity(s.Kind, p) == 1 ? 1 : s.Mode == LineMode.Amount ? 1 : s.Quantity;

    /// Checks everything that does not depend on the other lines. Returns null when the line is allowed.
    public static Error? Check(CartLineSpec s, CartProduct p, CartLimits limits, DateTimeOffset now)
    {
        _ = now;
        if (!p.IsPublished)
        {
            return CartErrors.ProductUnavailable;                                    // RUL-02
        }

        var kindError = s.Kind switch
        {
            LineKind.Buy => CheckBuy(s, p),
            LineKind.Sponsor => CheckSponsor(s, p, limits),
            LineKind.Donate => CheckDonate(s, p),
            _ => CartErrors.ModeNotAllowed,
        };
        if (kindError is not null)
        {
            return kindError;
        }

        var quantity = NormaliseQuantity(s, p);
        var max = MaxQuantity(s.Kind, p);
        if (quantity > max)
        {
            return CartErrors.QuantityLimit(max);
        }

        if (s.Kind == LineKind.Buy && p.Format is { Kind: BookFormatKind.Print } print && quantity > print.Available)
        {
            return CartErrors.OutOfStock(print.Available);                           // RUL-30
        }

        return CheckDedication(s, limits);                                           // RUL-21
    }

    /// Indicative unit price (RUL-15): catalogue price for units lines, the amount itself for amount lines.
    public static Money UnitPrice(CartLineSpec s, CartProduct p) => s.Mode == LineMode.Amount
        ? s.Amount ?? Money.Zero
        : s.Kind switch
        {
            LineKind.Buy => p.Type == ProductType.Course ? p.CoursePrice ?? Money.Zero : p.Format?.Price ?? Money.Zero,
            LineKind.Sponsor => p.Offer?.SeatPrice ?? Money.Zero,
            _ => p.Cause?.UnitAmount ?? Money.Zero,
        };

    /// Codes that would fail checkout now for an existing line (11 §3.1 `issues`).
    public static IReadOnlyList<string> Issues(CartLine line, CartProduct? p, CustomerOwnership owned, DateTimeOffset now)
    {
        if (p is null || !p.IsPublished)
        {
            return [ErrorCodes.CartProductUnavailable];
        }

        List<string> issues = [];
        switch (line.Kind)
        {
            case LineKind.Buy when p.Type == ProductType.Course:
                if (owned.Owns(p.ProductId))
                {
                    issues.Add(ErrorCodes.CartCourseOwned);
                }
                else if (owned.UnpaidOrderFor(p.ProductId) is not null)
                {
                    issues.Add(ErrorCodes.CartCourseInUnpaidOrder);
                }

                break;
            case LineKind.Buy:
                if (p.Format is not { IsActive: true })
                {
                    issues.Add(ErrorCodes.CartProductUnavailable);
                }
                else if (p.Format.Kind == BookFormatKind.Print && line.Quantity > p.Format.Available)
                {
                    issues.Add(ErrorCodes.CartOutOfStock);
                }

                break;
            case LineKind.Sponsor when p.Offer is not { IsOpen: true }:
            case LineKind.Donate when p.Cause is not { IsOpen: true }:
                issues.Add(ErrorCodes.CartCampaignEnded);
                break;
        }

        if (line.Intent == GiftIntent.Zakat && !(line.Kind == LineKind.Sponsor ? p.Offer?.ZakatEligible ?? false : p.Cause?.ZakatEligible ?? false))
        {
            issues.Add(ErrorCodes.CartZakatNotAllowed);
        }

        _ = now;
        return issues;
    }

    private static Error? CheckBuy(CartLineSpec s, CartProduct p)
    {
        if (s.Mode != LineMode.Units || s.Intent is not null || p.Type == ProductType.Cause)
        {
            return CartErrors.ModeNotAllowed;
        }

        if (s.Dedication is not null)
        {
            return CartErrors.DedicationNotAllowed;                                  // RUL-21
        }

        if (p.Type == ProductType.Course)
        {
            return p.CoursePrice is null ? CartErrors.ProductUnavailable : null;
        }

        // Book: an active format of this product; a digital format needs its private file (RUL-05).
        return p.Format is not { IsActive: true } f || (f.Kind == BookFormatKind.Digital && !f.HasFile)
            ? CartErrors.ProductUnavailable
            : null;
    }

    private static Error? CheckSponsor(CartLineSpec s, CartProduct p, CartLimits limits)
    {
        if (p.Type == ProductType.Cause || p.Offer is null || s.BookFormatId is not null)
        {
            return CartErrors.ModeNotAllowed;
        }

        if (!p.Offer.IsOpen)
        {
            return CartErrors.CampaignEnded;                                         // RUL-09, RUL-10
        }

        if (s.Mode == LineMode.Amount)
        {
            if (!p.Offer.AllowPartial)
            {
                return CartErrors.ModeNotAllowed;
            }

            if ((s.Amount ?? Money.Zero) < limits.MinGiftAmount)
            {
                return CartErrors.AmountBelowMinimum(limits.MinGiftAmount);
            }
        }

        return s.Intent == GiftIntent.Zakat && !p.Offer.ZakatEligible ? CartErrors.ZakatNotAllowed : null;   // RUL-19
    }

    private static Error? CheckDonate(CartLineSpec s, CartProduct p)
    {
        if (p.Type != ProductType.Cause || p.Cause is null || s.BookFormatId is not null)
        {
            return CartErrors.ModeNotAllowed;
        }

        if (!p.Cause.IsOpen)
        {
            return CartErrors.CampaignEnded;
        }

        if (s.Mode == LineMode.Units && p.Cause.UnitAmount is null)
        {
            return CartErrors.ModeNotAllowed;
        }

        if (s.Mode == LineMode.Amount)
        {
            if (!p.Cause.AllowCustomAmount)
            {
                return CartErrors.ModeNotAllowed;
            }

            if ((s.Amount ?? Money.Zero) < p.Cause.MinAmount)
            {
                return CartErrors.AmountBelowMinimum(p.Cause.MinAmount);
            }
        }

        return s.Intent == GiftIntent.Zakat && !p.Cause.ZakatEligible ? CartErrors.ZakatNotAllowed : null;
    }

    private static Error? CheckDedication(CartLineSpec s, CartLimits limits)
    {
        if (s.Dedication is not { } d)
        {
            return null;
        }

        if (d.ToName.Trim().Length is < 2 or > 60)
        {
            return CartErrors.DedicationInvalid("toName");
        }

        if (!SaudiMobile.TryNormalize(d.ToMobile, out _))
        {
            return CartErrors.DedicationInvalid("toMobile");
        }

        if (d.FromName is { Length: > 60 })
        {
            return CartErrors.DedicationInvalid("fromName");
        }

        if (!string.IsNullOrWhiteSpace(d.Text))
        {
            if (!limits.FreeTextEnabled)
            {
                return CartErrors.DedicationFreeTextDisabled;
            }

            return d.Text.Length > 140 ? CartErrors.DedicationInvalid("text") : null;
        }

        return d.TemplateKey is not null && limits.TemplateKeys.Contains(d.TemplateKey) ? null : CartErrors.DedicationInvalid("templateKey");
    }
}
