namespace Ta3lom.Store.Application.Interfaces;

/// The per-order status token handed to the browser at checkout (`GET /v1/orders/{reference}/status?t=`). Only its hash is
/// stored on the order.
public interface IOrderStatusTokens
{
    (string Token, string Hash) Create(string reference);

    /// Fixed-time comparison of a presented token with the stored hash.
    bool Matches(string token, string storedHash);
}
