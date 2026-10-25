using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Ta3lom.Store.Api.Extensions;

/// `t3s_cart`: the guest cart id, sealed with Data Protection; HttpOnly, Secure, SameSite=Lax,
/// path `/v1`, 30 days from the last change. A tampered or foreign value reads as "no cart".
internal sealed class CartCookie(IDataProtectionProvider dataProtection)
{
    public const string Name = "t3s_cart";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Store.Cart.v1");

    public Guid? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Guid.TryParseExact(_protector.Unprotect(value), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Write(HttpResponse response, Guid cartId) =>
        response.Cookies.Append(Name, _protector.Protect(cartId.ToString("N")), Options(TimeSpan.FromDays(30)));

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/v1",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
