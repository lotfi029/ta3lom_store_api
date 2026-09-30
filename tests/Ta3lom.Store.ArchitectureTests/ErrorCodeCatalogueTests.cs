using System.Reflection;
using System.Text.RegularExpressions;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.ArchitectureTests.Support;

namespace Ta3lom.Store.ArchitectureTests;

/// Every ErrorCodes constant is documented in the error table of 11 §1.6 (09 §10.3).
/// The reverse direction (every documented code has a constant) is a launch gate: codes arrive per session.
public sealed partial class ErrorCodeCatalogueTests
{
    [Fact]
    public void Every_error_code_is_documented_in_the_API_contract()
    {
        var documented = DocumentedCodes();
        var constants = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        constants.ShouldNotBeEmpty();
        constants.Where(c => !documented.Contains(c)).ShouldBeEmpty("Add the code to 11-api-contract.md §1.6 first");
    }

    [Fact]
    public void Error_codes_are_unique()
    {
        var constants = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        constants.Distinct(StringComparer.Ordinal).Count().ShouldBe(constants.Count);
    }

    private static HashSet<string> DocumentedCodes()
    {
        var text = File.ReadAllText(Assemblies.Doc("11-api-contract.md"));
        var start = text.IndexOf("### 1.6", StringComparison.Ordinal);
        var end = text.IndexOf("### 1.7", start, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);
        return Code().Matches(text[start..end]).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"`([a-z_]+(?:\.[a-z_]+)+)`")]
    private static partial Regex Code();
}
