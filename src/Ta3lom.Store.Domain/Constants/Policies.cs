namespace Ta3lom.Store.Domain.Constants;

/// Authorization policy names and their roles (11 §1.2, 09 §5.14, RUL-62).
public static class Policies
{
    /// Bearer access token with audience `store-customer`.
    public const string Customer = "customer";

    public const string StaffRead = "staff:read";
    public const string StaffCatalogue = "staff:catalogue";
    public const string StaffStock = "staff:stock";
    public const string StaffFulfilment = "staff:fulfilment";
    public const string StaffFinance = "staff:finance";
    public const string StaffRefund = "staff:refund";
    public const string StaffBeneficiaries = "staff:beneficiaries";
    public const string StaffAllocate = "staff:allocate";
    public const string StaffRevealPii = "staff:reveal-pii";
    public const string StaffResend = "staff:resend";
    public const string StaffAdmin = "staff:admin";

    /// Segregation of duties (RUL-62): `staff:refund`, `staff:allocate` and `staff:reveal-pii` are **not**
    /// satisfied by `admin`; the person must hold `finance` or `allocation_officer` explicitly.
    public static IReadOnlyDictionary<string, string[]> StaffRoles { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [StaffRead] = [.. Roles.All],
        [StaffCatalogue] = [Roles.Admin, Roles.StoreManager],
        [StaffStock] = [Roles.Admin, Roles.StoreManager, Roles.Fulfilment],
        [StaffFulfilment] = [Roles.Admin, Roles.Fulfilment],
        [StaffFinance] = [Roles.Admin, Roles.Finance],
        [StaffRefund] = [Roles.Finance],
        [StaffBeneficiaries] = [Roles.Admin, Roles.AllocationOfficer],
        [StaffAllocate] = [Roles.AllocationOfficer],
        [StaffRevealPii] = [Roles.AllocationOfficer],
        [StaffResend] = [Roles.Admin, Roles.Finance, Roles.StoreManager],
        [StaffAdmin] = [Roles.Admin],
    };
}

/// Authentication scheme names (09 §5.8): two JWT bearer schemes, same issuer, different audiences and keys.
public static class AuthSchemes
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";

    /// The 5-minute token between password and TOTP (audience `store-staff-mfa`).
    public const string StaffMfa = "StaffMfa";
}
