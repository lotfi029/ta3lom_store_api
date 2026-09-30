using Serilog.Core;
using Serilog.Events;

namespace Ta3lom.Store.Api.Logging;

/// Replaces sensitive values with "***" before any sink sees them (09 §8.1): properties, dictionary keys
/// and structure members whose name contains authorization, cookie, password, secret or token.
/// Mobile masking and OTP `code` redaction are added with identity (S-04).
public sealed class RedactingEnricher : ILogEventEnricher
{
    public const string Mask = "***";

    private static readonly string[] SensitiveFragments = ["authorization", "cookie", "password", "secret", "token"];

    private static readonly ScalarValue Masked = new(Mask);

    public static bool IsSensitive(string name) =>
        SensitiveFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var redacted = IsSensitive(name) ? Masked : Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case DictionaryValue dictionary:
                var entries = dictionary.Elements.Select(e => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    e.Key, e.Key.Value is string key && IsSensitive(key) ? Masked : Redact(e.Value))).ToList();
                return entries.Zip(dictionary.Elements).All(p => ReferenceEquals(p.First.Value, p.Second.Value))
                    ? value
                    : new DictionaryValue(entries);
            case StructureValue structure:
                var props = structure.Properties.Select(p => new LogEventProperty(p.Name, IsSensitive(p.Name) ? Masked : Redact(p.Value))).ToList();
                return props.Zip(structure.Properties).All(p => ReferenceEquals(p.First.Value, p.Second.Value))
                    ? value
                    : new StructureValue(props, structure.TypeTag);
            case SequenceValue sequence:
                var items = sequence.Elements.Select(Redact).ToList();
                return items.Zip(sequence.Elements).All(p => ReferenceEquals(p.First, p.Second)) ? value : new SequenceValue(items);
            default:
                return value;
        }
    }
}
