namespace Ta3lom.Store.Domain.Constants;

/// Authorization policy names and the staff roles that satisfy them.
public static class Policies
{
    /// Bearer access token with audience `store-customer`.
    public const string Customer = "customer";

    /// Cart routes: guests pass; a presented customer token must be valid (401 otherwise, so the web app refreshes).
    public const string CustomerOptional = "customer-optional";

    /// Every staff role: dashboard, read-only admin, donors and orders read.
    public const string StaffRead = "staff:read";

    /// Catalogue and media.
    public const string StaffCatalogue = "staff:catalogue";

    /// Block and unblock donors.
    public const string StaffCustomers = "staff:customers";

    /// Cancel pending orders.
    public const string StaffOrders = "staff:orders";

    /// Message templates.
    public const string StaffTemplates = "staff:templates";

    /// Staff users, settings and the audit log.
    public const string StaffAdmin = "staff:admin";

    public static IReadOnlyDictionary<string, string[]> StaffRoles { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [StaffRead] = [.. Roles.All],
        [StaffCatalogue] = [Roles.Admin, Roles.StoreManager],
        [StaffCustomers] = [Roles.Admin, Roles.StoreManager],
        [StaffOrders] = [Roles.Admin, Roles.Finance],
        [StaffTemplates] = [Roles.Admin, Roles.StoreManager],
        [StaffAdmin] = [Roles.Admin],
    };
}

/// Authentication scheme names: two JWT bearer schemes, same issuer, different audiences and keys.
public static class AuthSchemes
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";

    /// The 5-minute token between password and TOTP (audience `store-staff-mfa`).
    public const string StaffMfa = "StaffMfa";
}

/// Claims the API issues beyond the registered JWT ones.
public static class StoreClaims
{
    public const string Role = "role";

    /// Set while `staff_user.must_change_password` is true: only the password change and logout routes are allowed.
    public const string PasswordChangeRequired = "pwd_change";
}
