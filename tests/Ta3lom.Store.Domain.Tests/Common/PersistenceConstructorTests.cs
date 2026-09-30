using System.Reflection;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

/// The persistence mapper materialises rows through a non-public parameterless constructor (K-b: no EF types in Domain);
/// every mapped class must have one, and none may be public (state changes go through business methods).
public sealed class PersistenceConstructorTests
{
    private static readonly Assembly Domain = typeof(Cart).Assembly;

    private static IEnumerable<Type> MappedClasses() =>
        Domain.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }
                                     && t.Namespace is { } ns
                                     && (ns.StartsWith("Ta3lom.Store.Domain.Entities", StringComparison.Ordinal) || ns == "Ta3lom.Store.Domain.ValueObjects")
                                     && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null
                                     && !t.IsNested
                                     && t.GetMethod("<Clone>$") is null                                     // records are not mapped
                                     && !(t.IsAbstract && t.IsSealed));                                     // static classes

    [Fact]
    public void Every_mapped_class_has_a_private_parameterless_constructor_that_the_mapper_can_call()
    {
        var types = MappedClasses().ToList();
        types.Count.ShouldBeGreaterThan(40);

        foreach (var type in types)
        {
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
            ctor.ShouldNotBeNull($"{type.Name} has no non-public parameterless constructor");
            ctor.IsPrivate.ShouldBeTrue(type.Name);

            var instance = ctor.Invoke(null);

            instance.ShouldBeOfType(type);
            if (instance is Entity entity)
            {
                entity.Id.ShouldBe(Guid.Empty);
                entity.Equals(ctor.Invoke(null)).ShouldBeFalse($"{type.Name}: unsaved entities are never equal");
            }
        }
    }

    [Fact]
    public void No_mapped_class_has_a_public_parameterless_constructor() =>
        MappedClasses().Where(t => t.GetConstructor(Type.EmptyTypes) is not null).Select(t => t.Name).ShouldBeEmpty();
}
