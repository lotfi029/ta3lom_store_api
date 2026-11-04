namespace Ta3lom.Store.Domain.Enums;

/// How the donor paid, as the gateway reports it (mada, a scheme card, Apple Pay, STC Pay). Snake_case text.
public enum PaymentMethod
{
    Unknown,
    Mada,
    Creditcard,
    Applepay,
    Stcpay,
}
