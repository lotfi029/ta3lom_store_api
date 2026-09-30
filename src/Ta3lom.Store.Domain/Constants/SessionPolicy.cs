namespace Ta3lom.Store.Domain.Constants;

/// Token and code lifetimes (RUL-60, RUL-62, 09 §5.8, 11 §1.2).
public static class SessionPolicy
{
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan OtpMobileWindow = TimeSpan.FromMinutes(15);
    public const int OtpMaxPerMobile = 3;
    public static readonly TimeSpan OtpIpWindow = TimeSpan.FromHours(1);
    public const int OtpMaxPerIp = 10;
    public static readonly TimeSpan OtpRetention = TimeSpan.FromHours(24);

    /// SMS-pumping guard (08 §6.2): codes issued store-wide per Riyadh day; above it requests get 429 and staff one alert.
    public const int OtpDailyGlobalCap = 2000;

    public static readonly TimeSpan CustomerAccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CustomerRefreshLifetime = TimeSpan.FromDays(60);
    public static readonly TimeSpan StaffAccessLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan StaffRefreshLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan StaffMfaLifetime = TimeSpan.FromMinutes(5);

    /// Two tabs refreshing with the same cookie within this window get 409 `auth.refresh_race`, not a family revoke.
    public static readonly TimeSpan RefreshRaceGrace = TimeSpan.FromSeconds(20);
}
