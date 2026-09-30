using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Infrastructure.Persistence.Converters;

/// Money ↔ numeric(12,2) (RUL-31).
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(m => m.Amount, d => new Money(d));
