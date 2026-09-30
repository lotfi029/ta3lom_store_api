namespace Ta3lom.Store.Application.DTOs.Dev;

/// `POST /dev/orders/{ref}/confirm-payment` body (doc 15 §2.4): omitted amount = the order's grand total.
public sealed record DevConfirmPaymentRequest(string? Amount, string? ConfirmationId);

/// `outcome`: `paid` | `already_paid` | `payment_review` | `not_found`.
public sealed record DevSettlementResponse(string Reference, string Outcome, string Status);

/// `POST /dev/lms/progress` (Development/Staging): what the internal LMS "reports" for an enrolment on the next
/// `LmsProgressSync` run. `completed: true` completes it at the current time.
public sealed record DevLmsProgressRequest(Guid EnrolmentId, short ProgressPct, bool Completed, string? CertificateNo);
