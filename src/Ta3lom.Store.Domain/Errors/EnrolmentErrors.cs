namespace Ta3lom.Store.Domain.Errors;

/// Course access (11 §1.6, RUL-42).
public static class EnrolmentErrors
{
    public static Error NotActive => Error.Conflict(ErrorCodes.EnrolmentNotActive, "The enrolment is not active.");
}
