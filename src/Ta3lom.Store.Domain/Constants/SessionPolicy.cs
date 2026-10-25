namespace Ta3lom.Store.Domain.Constants;

/// Token and code lifetimes (D-10, D-11, D-12, D-14).
public static class SessionPolicy
{
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan OtpMobileWindow = TimeSpan.FromMinutes(15);
    public const int OtpMaxPerMobile = 3;
    public static readonly TimeSpan OtpIpWindow = TimeSpan.FromHours(1);
    public const int OtpMaxPerIp = 10;
    public static readonly TimeSpan OtpRetention = TimeSpan.FromHours(24);

    /// SMS-pumping guard: codes issued store-wide per Riyadh day. Staff get one alert at half of it; above it requests get 429.
    public const int OtpDailyGlobalCap = 2000;

    public static readonly TimeSpan CustomerAccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan StaffAccessLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan StaffMfaLifetime = TimeSpan.FromMinutes(5);

    /// Absolute lifetime of a refresh-token family: rotation never extends it (D-11).
    public static readonly TimeSpan CustomerFamilyLifetime = TimeSpan.FromDays(60);
    public static readonly TimeSpan StaffFamilyLifetime = TimeSpan.FromHours(8);

    /// One refresh token of a customer family lives at most this long (and never past the family).
    public static readonly TimeSpan CustomerRefreshLifetime = TimeSpan.FromDays(30);

    /// Two tabs refreshing with the same cookie within this window get 409 `auth.refresh_race`, not a family revoke.
    public static readonly TimeSpan RefreshRaceGrace = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    /// A-5: an `in_progress` claim older than this was left by a crash; the next request with the key takes it over.
    public static readonly TimeSpan IdempotencyStaleClaim = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan GuestCartIdle = TimeSpan.FromDays(30);
}
