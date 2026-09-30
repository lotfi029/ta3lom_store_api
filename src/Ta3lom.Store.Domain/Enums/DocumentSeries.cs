using System.Runtime.Serialization;

namespace Ta3lom.Store.Domain.Enums;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum DocumentSeries
{
    [EnumMember(Value = "INV")]
    Inv,
    [EnumMember(Value = "CN")]
    Cn,
    [EnumMember(Value = "DR")]
    Dr,
    [EnumMember(Value = "DRC")]
    Drc,
}
