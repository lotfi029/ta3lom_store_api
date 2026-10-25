namespace Ta3lom.Store.Domain.Constants;

/// Staff roles: the four values of the `staff_user.roles` CHECK. A staff member may hold several.
public static class Roles
{
    public const string Admin = "admin";
    public const string StoreManager = "store_manager";
    public const string Finance = "finance";
    public const string Viewer = "viewer";

    public static IReadOnlyList<string> All { get; } = [Admin, StoreManager, Finance, Viewer];

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}
