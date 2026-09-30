namespace Ta3lom.Store.Domain.Constants;

/// Staff roles (RUL-62): the six values of the `staff_user.roles` CHECK. A user may hold several.
public static class Roles
{
    public const string Admin = "admin";
    public const string StoreManager = "store_manager";
    public const string Finance = "finance";
    public const string AllocationOfficer = "allocation_officer";
    public const string Fulfilment = "fulfilment";
    public const string Viewer = "viewer";

    public static IReadOnlyList<string> All { get; } = [Admin, StoreManager, Finance, AllocationOfficer, Fulfilment, Viewer];

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}
