using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Domain.Enums;

namespace Ta3lom.Store.Infrastructure.Persistence.Converters;

/// Enum ↔ the DDL's text value via <see cref="EnumWire"/>.
internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(v => v.ToWire(), v => EnumWire.Parse<TEnum>(v))
    where TEnum : struct, Enum;
