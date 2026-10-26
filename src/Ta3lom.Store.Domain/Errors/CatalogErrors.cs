namespace Ta3lom.Store.Domain.Errors;

/// Categories and projects (BR-A01 … BR-A06, D-05 … D-08).
public static class CatalogErrors
{
    public static Error CategoryNotFound => Error.NotFound(ErrorCodes.CategoryNotFound, "Category not found.");

    public static Error CategorySlugTaken => Error.Conflict(ErrorCodes.CategorySlugTaken, "The category slug is taken.");

    public static Error ProjectNotFound => Error.NotFound(ErrorCodes.ProjectNotFound, "Project not found.");

    public static Error ProjectSlugTaken => Error.Conflict(ErrorCodes.ProjectSlugTaken, "The project slug is taken (or kept for a redirect).");

    /// D-06: `missing` lists the absent requirements (`titleAr`, `summaryAr`, `bodyAr`, `cover`, `category`, `amount`).
    public static Error PublishIncomplete(IReadOnlyList<string> missing) =>
        Error.Unprocessable(ErrorCodes.ProjectPublishIncomplete, "The project cannot be published yet.").With("missing", missing);

    /// D-07: allowed are draft → published, published → archived, archived → published.
    public static Error InvalidTransition(ProjectStatus from, ProjectStatus to) =>
        Error.Conflict(ErrorCodes.ProjectInvalidTransition, "This status change is not allowed.")
            .With("from", from.ToWire()).With("to", to.ToWire());

    /// L-13: a deactivated option (used in an order) keeps its label, so no other option of the project may take it.
    public static Error OptionLabelTaken(string label) =>
        Error.Unprocessable(ErrorCodes.ProjectOptionLabelTaken, "A deactivated option of this project keeps this label.").With("label", label);

    /// A cover or category image must be a public image asset.
    public static Error ImageRequired => Error.Unprocessable(ErrorCodes.MediaImageRequired, "The asset is not a public image.");
}

/// The donation cart (BR-A07, D-01 … D-05).
public static class CartErrors
{
    public static Error TargetUnavailable => Error.Unprocessable(ErrorCodes.CartTargetUnavailable, "The project or category does not accept gifts now.");

    public static Error OptionUnavailable => Error.Unprocessable(ErrorCodes.CartOptionUnavailable, "The donation option is not available.");

    public static Error OpenAmountNotAllowed => Error.Unprocessable(ErrorCodes.CartOpenAmountNotAllowed, "This project accepts its options only.");

    public static Error AmountBelowMinimum(Money minimum) =>
        Error.Unprocessable(ErrorCodes.CartAmountBelowMinimum, "The amount is below the minimum gift.").With("min", minimum.ToWire());

    public static Error AmountAboveMaximum(Money maximum) =>
        Error.Unprocessable(ErrorCodes.CartAmountAboveMaximum, "The amount is above the maximum for one line.").With("max", maximum.ToWire());

    public static Error QuantityLimit(int max) => Error.Unprocessable(ErrorCodes.CartQuantityLimit, "The quantity is out of range.").With("max", max);

    public static Error TooManyLines(int max) => Error.Unprocessable(ErrorCodes.CartTooManyLines, "The cart has too many lines.").With("max", max);

    public static Error TotalExceeded(Money max) =>
        Error.Unprocessable(ErrorCodes.CartTotalExceeded, "The cart total would exceed the limit.").With("max", max.ToWire());

    public static Error LineNotFound => Error.NotFound(ErrorCodes.CartLineNotFound, "Cart line not found.");
}

/// Checkout and donation orders (BR-A08, D-09).
public static class OrderErrors
{
    public static Error CartEmpty => Error.Conflict(ErrorCodes.CheckoutCartEmpty, "The cart is empty.");

    /// Re-validation failed; `problems` lists each line with its error code.
    public static Error LinesInvalid(IReadOnlyList<object> problems) =>
        Error.Unprocessable(ErrorCodes.CheckoutLinesInvalid, "Some lines cannot be donated to now.").With("problems", problems);

    public static Error ContactRequired => Error.Unprocessable(ErrorCodes.CheckoutContactRequired, "Name and e-mail are required.");

    public static Error NotFound => Error.NotFound(ErrorCodes.OrderNotFound, "Order not found.");

    public static Error CancelNotAllowed(OrderStatus status) =>
        Error.Conflict(ErrorCodes.OrderCancelNotAllowed, "Only a pending order can be cancelled.").With("orderStatus", status.ToWire());
}
