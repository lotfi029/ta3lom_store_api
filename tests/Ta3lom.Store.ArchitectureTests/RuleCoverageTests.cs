using System.Reflection;
using System.Text.RegularExpressions;
using Ta3lom.Store.ArchitectureTests.Support;
using Ta3lom.Store.Testing;

namespace Ta3lom.Store.ArchitectureTests;

/// Every rule a session has implemented (ImplementedRules.txt, plan 14 §13.1) has at least one
/// [Rule("RUL-nn")] test, and every [Rule] names a rule that exists in 03-business-rules.md.
public sealed partial class RuleCoverageTests
{
    private static readonly HashSet<string> RulesInDocs = ReadRulesFromDocs();

    [Fact]
    public void Business_rules_document_defines_RUL_01_to_RUL_63()
    {
        var expected = Enumerable.Range(1, 63).Select(n => $"RUL-{n:00}");

        expected.Where(r => !RulesInDocs.Contains(r)).ShouldBeEmpty();
    }

    [Fact]
    public void Implemented_rules_exist_in_the_business_rules_document() =>
        ImplementedRules().Where(r => !RulesInDocs.Contains(r)).ShouldBeEmpty();

    [Fact]
    public void Every_implemented_rule_has_a_test()
    {
        var tested = RuleIdsOnTests().Select(t => t.Rule).ToHashSet(StringComparer.Ordinal);

        ImplementedRules().Where(r => !tested.Contains(r)).ShouldBeEmpty("Add a test tagged [Rule(\"RUL-nn\")] for each rule listed in ImplementedRules.txt");
    }

    [Fact]
    public void Every_Rule_attribute_names_a_real_rule()
    {
        var unknown = RuleIdsOnTests().Where(t => !RulesInDocs.Contains(t.Rule)).Select(t => $"{t.Test}: {t.Rule}");

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Rule_attributes_are_found() =>
        RuleIdsOnTests().ShouldContain(t => t.Rule == "RUL-31");   // Money tests (S-01), proves the scan works

    private static IReadOnlyList<string> ImplementedRules() =>
        [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ImplementedRules.txt"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))];

    private static IEnumerable<(string Test, string Rule)> RuleIdsOnTests() =>
        Assemblies.Tests
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(m => m.GetCustomAttributes<RuleAttribute>().Select(r => ($"{m.DeclaringType!.Name}.{m.Name}", r.Id)));

    private static HashSet<string> ReadRulesFromDocs() =>
        RuleId().Matches(File.ReadAllText(Assemblies.Doc("03-business-rules.md"))).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"RUL-\d{2}")]
    private static partial Regex RuleId();
}
