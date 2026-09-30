namespace Ta3lom.Store.Api.Extensions;

/// Session cookies (11 §1.2): HttpOnly, Secure, SameSite=Lax, host-only.
public static class AuthCookies
{
    public const string CustomerRefresh = "t3s_rt";
    public const string CustomerRefreshPath = "/v1/auth";
    public const string StaffRefresh = "t3s_srt";
    public const string StaffRefreshPath = "/v1/admin/auth";

    public static void Set(HttpResponse response, string name, string path, string value, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(name, value, Options(path, expiresAt - now));
    }

    public static void Clear(HttpResponse response, string name, string path)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(name, Options(path, null));
    }

    private static CookieOptions Options(string path, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = path,
        MaxAge = maxAge,
        IsEssential = true,
    };
}
