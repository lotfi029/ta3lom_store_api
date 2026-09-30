namespace Ta3lom.Store.Application.Interfaces;

/// Per-order status token (11 §6.3): `st_` + HMAC of the order id, so the thanks page can poll without a session.
public interface IOrderStatusTokens
{
    string Issue(Guid orderId);

    bool Verify(Guid orderId, string? token);
}
