namespace Ta3lom.Store.Application.Interfaces;

/// Explicit `audit_log` rows for security events (BR-A13): sign-ins, lockouts, TOTP failures, token reuse, blocks. Staff data
/// changes are audited by the save interceptor; these events often have no staff actor yet. The row joins the current unit
/// of work (it is saved with the next SaveChanges).
public interface IAuditTrail
{
    /// <paramref name="actor"/>: a staff e-mail, `customer:{id}`, `system` or `anonymous`; null = the current actor.
    void Record(string action, string entity, string? entityId, string? actor = null, object? details = null);
}

/// The security event names of BR-A13.
public static class AuditActions
{
    public const string StaffLoginSucceeded = "staff.login_succeeded";
    public const string StaffLoginFailed = "staff.login_failed";
    public const string StaffLocked = "staff.locked";
    public const string StaffTotpFailed = "staff.totp_failed";
    public const string StaffPasswordChanged = "staff.password_changed";
    public const string TokenReuseDetected = "token.reuse_detected";
    public const string CustomerBlocked = "customer.blocked";
    public const string CustomerUnblocked = "customer.unblocked";
    public const string CustomerDeleted = "customer.deleted";
    public const string OrderCancelled = "order.cancelled";
}
