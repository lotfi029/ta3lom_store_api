using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Persistence.Converters;

namespace Ta3lom.Store.Infrastructure.Persistence.Interceptors;

/// Writes one `audit_log` row per entity a staff actor adds, changes or deletes, in the same SaveChanges
/// (09 §3.4, BR-S11). `diff` lists the changed columns; personal data and secrets are written as "***".
internal sealed class AuditSaveChangesInterceptor(ICurrentActor actor, IClock clock) : SaveChangesInterceptor
{
    public const string Mask = "***";

    private static readonly string[] SecretFragments = ["hash", "secret", "token", "password"];

    private static readonly string[] PersonalSuffixes = ["mobile", "email", "name", "address"];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditRows(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddAuditRows(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddAuditRows(DbContext? context)
    {
        if (context is null || actor.Kind != ActorKind.Staff)
        {
            return;
        }

        var now = clock.UtcNow;
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && e.Entity is not AuditLogEntry && e.Metadata.FindPrimaryKey() is not null)
            .ToList();

        foreach (var entry in entries)
        {
            var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
            var action = $"{table}.{entry.State.ToString().ToLowerInvariant()}";
            var key = string.Join(',', entry.Metadata.FindPrimaryKey()!.Properties.Select(p => Format(entry.Property(p.Name).CurrentValue)));
            context.Add(new AuditLogEntry(actor.Name ?? "staff", action, table, now, key, Diff(entry), actor.IpHash));
        }
    }

    private static string Diff(EntityEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in entry.Properties)
            {
                if (entry.State == EntityState.Modified && !property.IsModified)
                {
                    continue;
                }

                var column = property.Metadata.GetColumnName();
                writer.WritePropertyName(column);
                if (IsSensitive(property))
                {
                    writer.WriteStringValue(Mask);
                    continue;
                }

                var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
                if (value is not null && property.Metadata.GetValueConverter() is { } converter)
                {
                    value = converter.ConvertToProvider(value);   // the stored value, e.g. "pending_payment"
                }

                if (value is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(Format(value));
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsSensitive(PropertyEntry property)
    {
        var name = property.Metadata.Name;
        return property.Metadata.GetValueConverter() is ProtectedStringConverter
               || property.Metadata.ClrType == typeof(byte[])
               || SecretFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))
               || PersonalSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        Money m => m.ToWire(),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        string[] a => string.Join(',', a),
        _ => value.ToString() ?? string.Empty,
    };
}
