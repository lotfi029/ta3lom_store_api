using System.Runtime.Serialization;

namespace Ta3lom.Store.Domain.Payments;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum PaymentMethod
{
    Mada,
    [EnumMember(Value = "applepay")]
    ApplePay,
    Card,
    [EnumMember(Value = "stcpay")]
    StcPay,
    Bnpl,
}
