namespace Ta3lom.Store.Domain.Errors;

/// Allocation and unit lifecycle (11 §1.6, RUL-47 … RUL-50).
public static class AllocationErrors
{
    public static Error NoUnits => Error.Conflict(ErrorCodes.AllocationNoUnits, "No units are awaiting allocation for this product.");

    /// Concurrent allocation hit `ux_enrolment_live`; nothing was allocated, the officer retries.
    public static Error Conflict => Error.Conflict(ErrorCodes.AllocationConflict, "A beneficiary received this course meanwhile; retry.");

    /// 422: every failing beneficiary at once (`errors[]` of `{ beneficiaryCode, code }`); nothing was allocated.
    public static Error Rejected(IReadOnlyList<AllocationRejection> errors) =>
        Error.Unprocessable(ErrorCodes.AllocationRejected, "One or more beneficiaries fail RUL-47; nothing was allocated.").With("errors", errors);

    public static Error UnitInvalidTransition => Error.Conflict(ErrorCodes.UnitInvalidTransition, "The unit cannot move to that state.");
}

/// One failing beneficiary inside `allocation.rejected`.
public sealed record AllocationRejection(string BeneficiaryCode, string Code);
