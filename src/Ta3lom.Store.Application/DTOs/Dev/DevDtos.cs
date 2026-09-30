namespace Ta3lom.Store.Application.DTOs.Dev;

/// `POST /dev/orders/{ref}/confirm-payment` body (doc 15 §2.4): omitted amount = the order's grand total.
public sealed record DevConfirmPaymentRequest(string? Amount, string? ConfirmationId);

/// `outcome`: `paid` | `already_paid` | `payment_review` | `not_found`.
public sealed record DevSettlementResponse(string Reference, string Outcome, string Status);
