using Xunit.v3;

namespace Ta3lom.Store.Testing;

/// Tags a test with the business rule it proves (xUnit trait `Rule=D-nn`).
/// RuleCoverageTests reads these attributes.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RuleAttribute(string id) : Attribute, ITraitAttribute
{
    public string Id { get; } = id;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("Rule", Id)];
}
