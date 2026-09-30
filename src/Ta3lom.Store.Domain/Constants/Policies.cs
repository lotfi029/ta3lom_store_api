namespace Ta3lom.Store.Domain.Constants;

/// Authorization policy names (11 §1.2). Staff policies and their role sets arrive with S-05.
public static class Policies
{
    /// Bearer access token with audience `store-customer`.
    public const string Customer = "customer";
}

/// Authentication scheme names (09 §5.8): two JWT bearer schemes, same issuer, different audiences and keys.
public static class AuthSchemes
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
}
