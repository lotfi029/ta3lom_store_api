using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Domain.Services;

/// RUL-47 (a)–(e) (09 §5.6); a null result means allowed. `CheckPerson` holds the checks that do not depend on the unit
/// (a, c, e) and runs first for every selected beneficiary; `CheckPair` adds the unit's offer group (b) and zakat (d).
public static class AllocationPolicy
{
    public static string? CheckPerson(Beneficiary b, bool isCourse, bool hasLiveEnrolment)
    {
        ArgumentNullException.ThrowIfNull(b);
        if (b.EligibilityStatus != EligibilityStatus.Approved)
        {
            return ErrorCodes.AllocationNotApproved;                                           // (a)
        }

        if (isCourse && hasLiveEnrolment)
        {
            return ErrorCodes.AllocationAlreadyEnrolled;                                       // (c)
        }

        return isCourse && b.Mobile is null ? ErrorCodes.AllocationMobileRequired : null;     // (e) own or guardian's mobile
    }

    public static string? CheckPair(Beneficiary b, string offerGroup, SponsoredUnit unit)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(unit);
        if (!string.Equals(b.BeneficiaryGroup, offerGroup, StringComparison.Ordinal))
        {
            return ErrorCodes.AllocationGroupMismatch;                                         // (b)
        }

        return unit.HasZakat && !b.ZakatEligible ? ErrorCodes.AllocationZakatMismatch : null;  // (d)
    }
}
