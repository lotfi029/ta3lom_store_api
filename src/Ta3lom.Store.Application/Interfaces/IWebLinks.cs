namespace Ta3lom.Store.Application.Interfaces;

/// Links into the donations web site (`Store:WebBaseUrl`).
public interface IWebLinks
{
    /// `{web}/{locale}/orders/{ref}?t={statusToken}` — the order status page a guest can open without a session.
    string OrderStatus(string locale, string reference, string statusToken);

    /// `{web}/{locale}/projects/{slug}`.
    string Project(string locale, string slug);
}
