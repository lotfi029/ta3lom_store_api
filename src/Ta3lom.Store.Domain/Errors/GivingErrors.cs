namespace Ta3lom.Store.Domain.Errors;

/// Dedication cards and cause updates (11 §1.6, RUL-22, RUL-54).
public static class GivingErrors
{
    public static Error CardNotFound => Error.NotFound(ErrorCodes.CardNotFound, "No dedication card for this token.");

    public static Error CauseUpdateNotFound => Error.NotFound(ErrorCodes.NotFound, "Cause update not found.");

    public static Error DedicationNotFound => Error.NotFound(ErrorCodes.NotFound, "Dedication not found.");

    public static Error GoalAdjustmentInvalid => Error.Unprocessable(ErrorCodes.GoalAdjustmentInvalid, "Empty correction, or a counter would go negative.");

    public static Error CauseUpdateAlreadyNotified => Error.Conflict(ErrorCodes.CauseUpdateAlreadyNotified, "Donors were already notified of this update.");
}
