using System.Reflection;
using Ta3lom.Store.ArchitectureTests.Support;

namespace Ta3lom.Store.ArchitectureTests;

/// Domain entities protect their state: no public setters, no persistence attributes, a private parameterless constructor
/// for EF Core, and a creation path that sets the required state (a constructor or a static factory; child entities are
/// created by their aggregate root through an internal one).
public sealed class DomainModelTests
{
    private static readonly Type[] Entities = [.. Assemblies.Domain.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                    && t.Namespace?.StartsWith("Ta3lom.Store.Domain.Entities.", StringComparison.Ordinal) == true
                    && t.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes) is not null)];

    [Fact]
    public void Every_table_has_an_entity() => Entities.Length.ShouldBe(18);   // 20 tables; idempotency_key and job_run are Infrastructure records

    [Fact]
    public void Entities_have_no_public_setters()
    {
        var offenders = Entities.SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod is { IsPublic: true }).Select(p => $"{t.Name}.{p.Name}"));

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Entities_carry_no_persistence_or_validation_attributes()
    {
        var offenders = Entities.SelectMany(t => t.GetProperties().Cast<MemberInfo>().Append(t))
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Namespace is { } ns
                && (ns.StartsWith("System.ComponentModel", StringComparison.Ordinal) || ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))))
            .Select(m => m.Name);

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Entities_have_a_creation_path_besides_the_EF_constructor() =>
        Entities.Where(t =>
                !t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(c => !c.IsPrivate)
                && !t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.ReturnType == t && !m.IsPrivate)
                && !t.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Any(c => c.IsPrivate && c.GetParameters().Length > 0))
            .Select(t => t.Name).ShouldBeEmpty();
}
