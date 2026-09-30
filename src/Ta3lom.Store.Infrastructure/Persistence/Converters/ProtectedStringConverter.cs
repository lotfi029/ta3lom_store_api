using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Infrastructure.Persistence.Converters;

/// Beneficiary PII: plaintext only in memory, Data Protection ciphertext in the bytea column (database/01 §6).
/// Values are never logged; ciphertext is non-deterministic, so it is never used in WHERE (lookups use mobile_hash).
internal sealed class ProtectedStringConverter(IPiiProtector pii)
    : ValueConverter<string, byte[]>(plain => pii.Protect(plain), cipher => pii.Unprotect(cipher));
