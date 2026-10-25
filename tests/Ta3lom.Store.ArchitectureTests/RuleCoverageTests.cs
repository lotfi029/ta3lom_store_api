using System.Reflection;
using Ta3lom.Store.Testing;

namespace Ta3lom.Store.ArchitectureTests;

/// Every business rule D-01 … D-14 is proven by at least one test tagged `[Rule("D-xx")]` in the test assemblies, and no
/// tag names a rule that does not exist.
public sealed class RuleCoverageTests
{
    private static readonly string[] Rules = [.. Enumerable.Range(1, 14).Select(n => $"D-{n:D2}")];

    private static readonly Assembly[] TestAssemblies =
    [
        typeof(Ta3lom.Store.Domain.Tests.ValueObjects.SaudiMobileTests).Assembly,
        typeof(Ta3lom.Store.Application.Tests.Abstractions.ResultTests).Assembly,
        typeof(Ta3lom.Store.Api.IntegrationTests.Infrastructure.StoreApiFactory).Assembly,
    ];

    private static List<string> Tagged() =>
    [
        .. TestAssemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<RuleAttribute>()).Select(r => r.Id),
    ];

    [Fact]
    public void Every_rule_has_a_test() => Rules.Except(Tagged()).ShouldBeEmpty();

    [Fact]
    public void Rule_tags_name_known_rules() => Tagged().Distinct().Except(Rules).ShouldBeEmpty();
}
