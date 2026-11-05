namespace Ta3lom.Store.Domain.Errors;

/// Showcase mode: a store feature switched off by configuration (`Store:Payments:Provider = none`,
/// `Store:Features:CustomerSignIn = false`). 409: the request is valid, the store does not take it yet.
public static class StoreErrors
{
    public static Error PaymentsDisabled => Error.Conflict(ErrorCodes.StorePaymentsDisabled, "Online donations are not open yet.");

    public static Error SignInDisabled => Error.Conflict(ErrorCodes.StoreSignInDisabled, "Donor sign-in is not open yet.");
}
