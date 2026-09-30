using System.Reflection;

namespace Ta3lom.Store.ArchitectureTests.Support;

/// Finds money-like members (price, amount, total, fee, balance, money) typed as double or float (RUL-31).
public static class MoneyMemberScanner
{
    private static readonly string[] MoneyWords = ["price", "amount", "total", "fee", "balance", "money"];

    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<string> FindFloatingPointMoney(params Assembly[] assemblies) =>
        [.. assemblies.SelectMany(SafeTypes).SelectMany(Scan).Distinct(StringComparer.Ordinal)];

    public static IEnumerable<string> Scan(Type type)
    {
        foreach (var p in type.GetProperties(Members).Where(p => IsMoneyName(p.Name) && IsFloating(p.PropertyType)))
        {
            yield return $"{type.FullName}.{p.Name}";
        }

        foreach (var f in type.GetFields(Members).Where(f => !f.Name.StartsWith('<') && IsMoneyName(f.Name) && IsFloating(f.FieldType)))
        {
            yield return $"{type.FullName}.{f.Name}";
        }

        foreach (var m in type.GetMethods(Members).Cast<MethodBase>().Concat(type.GetConstructors(Members)))
        {
            foreach (var p in m.GetParameters().Where(p => p.Name is not null && IsMoneyName(p.Name) && IsFloating(p.ParameterType)))
            {
                yield return $"{type.FullName}.{m.Name}({p.Name})";
            }
        }
    }

    private static bool IsMoneyName(string name) => MoneyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static bool IsFloating(Type t)
    {
        var type = Nullable.GetUnderlyingType(t) ?? t;
        return type == typeof(double) || type == typeof(float);
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
