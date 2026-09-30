namespace Ta3lom.Store.Application.Interfaces;

/// Links into the ta3lom.sa storefront (`Store:WebBaseUrl`): the gateway return page and public dedication cards.
public interface IWebLinks
{
    /// `{web}/{locale}/store/orders/{ref}/thanks?t={statusToken}` (doc 15 §3 ReturnUrl).
    string OrderThanks(string locale, string reference, string statusToken);

    /// `{web}/{locale}/store/card/{token}` (RUL-22).
    string Card(string locale, string cardToken);

    /// `{web}/{locale}/learn/{slug}?t={token}` — the internal course player (InternalAccessLmsGateway).
    string Learn(string locale, string courseSlug, string token);
}
