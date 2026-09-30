namespace Ta3lom.Store.Domain.Errors;

/// Beneficiary register (11 §1.6, RUL-51, RUL-52).
public static class BeneficiaryErrors
{
    public static Error NotFound => Error.NotFound(ErrorCodes.BeneficiaryNotFound, "Beneficiary not found.");

    public static Error MobileExists => Error.Conflict(ErrorCodes.BeneficiaryMobileExists, "This mobile belongs to another beneficiary.");

    public static Error NoLiveSeat => Error.Conflict(ErrorCodes.BeneficiaryNoLiveSeat, "The beneficiary has no allocated course seat.");

    /// 422 with the row-level report (`errors[]`, 11 §9.13); nothing was saved.
    public static Error ImportInvalid(object report) =>
        Error.Unprocessable(ErrorCodes.BeneficiaryImportInvalid, "CSV rows are invalid; nothing was imported.").With("report", report);
}
