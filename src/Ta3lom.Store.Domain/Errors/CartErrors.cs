namespace Ta3lom.Store.Domain.Errors;

/// Cart line rules (11 §1.6 "Cart", RUL-12 … RUL-21, RUL-30).
public static class CartErrors
{
    public static Error ProductUnavailable => Error.Conflict(ErrorCodes.CartProductUnavailable, "The product or format is not available.");

    public static Error ModeNotAllowed => Error.Unprocessable(ErrorCodes.CartModeNotAllowed, "This kind or mode is not allowed for the product.");

    public static Error QuantityLimit(int max) => Error.Unprocessable(ErrorCodes.CartQuantityLimit, "The quantity is above the line maximum.").With("max", max);

    public static Error AmountBelowMinimum(Money min) =>
        Error.Unprocessable(ErrorCodes.CartAmountBelowMinimum, "The amount is below the minimum.").With("min", min.ToWire());

    public static Error CampaignEnded => Error.Conflict(ErrorCodes.CartCampaignEnded, "The offer or cause is closed.");

    public static Error DuplicateCourse => Error.Conflict(ErrorCodes.CartDuplicateCourse, "The course is already in the cart.");

    public static Error CourseOwned => Error.Conflict(ErrorCodes.CartCourseOwned, "The customer is already enrolled in this course.");

    public static Error CourseInUnpaidOrder(string orderRef) =>
        Error.Conflict(ErrorCodes.CartCourseInUnpaidOrder, "The course is in an unpaid order.").With("orderRef", orderRef);

    public static Error ZakatNotAllowed => Error.Unprocessable(ErrorCodes.CartZakatNotAllowed, "Zakat is not allowed for this product.");

    public static Error DedicationNotAllowed => Error.Unprocessable(ErrorCodes.CartDedicationNotAllowed, "Purchases cannot be dedicated.");

    public static Error DedicationInvalid(string field) =>
        Error.BadRequest(ErrorCodes.CartDedicationInvalid, "The dedication is invalid.").With("field", field);

    public static Error DedicationFreeTextDisabled => Error.Unprocessable(ErrorCodes.CartDedicationFreeTextDisabled, "Free-text dedications are disabled.");

    public static Error OutOfStock(int available) => Error.Conflict(ErrorCodes.CartOutOfStock, "Not enough stock.").With("available", available);

    public static Error TooManyLines => Error.Unprocessable(ErrorCodes.CartTooManyLines, "The cart has too many lines.");

    public static Error TotalExceeded(Money max) => Error.Unprocessable(ErrorCodes.CartTotalExceeded, "The cart total is above the limit.").With("max", max.ToWire());

    public static Error LineNotFound => Error.NotFound(ErrorCodes.CartLineNotFound, "The line is not in this cart.");
}
