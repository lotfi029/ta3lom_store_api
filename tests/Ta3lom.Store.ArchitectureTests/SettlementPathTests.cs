using System.Reflection;
using Ta3lom.Store.ArchitectureTests.Support;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Testing;

namespace Ta3lom.Store.ArchitectureTests;

/// T-2 (plan 14 S-16, doc 15 §2): `IOrderSettlement` is the only code path that sets an order `paid`. `Order.Status` has a
/// private setter, so the only way in is `Order.MarkPaid`; the IL of every production method is scanned for calls to it.
public sealed class SettlementPathTests
{
    private static readonly MethodInfo MarkPaid = typeof(Order).GetMethod(nameof(Order.MarkPaid))!;

    [Fact]
    [Rule("RUL-39")]
    public void Only_OrderSettlement_calls_Order_MarkPaid()
    {
        var callers = Assemblies.Production
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
            .Where(Calls)
            .Select(m => Outermost(m.DeclaringType!).FullName)
            .Distinct()
            .ToList();

        callers.ShouldBe(["Ta3lom.Store.Application.Features.Settlement.OrderSettlement"]);
    }

    [Fact]
    public void Order_status_has_no_public_setter() =>
        typeof(Order).GetProperty(nameof(Order.Status))!.SetMethod!.IsPublic.ShouldBeFalse();

    private static Type Outermost(Type t) => t.DeclaringType is { } d ? Outermost(d) : t;

    /// call (0x28) / callvirt (0x6F) followed by a method token that resolves to `Order.MarkPaid`.
    private static bool Calls(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return false;
        }

        if (il is null)
        {
            return false;
        }

        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            if ((token >> 24) is not (0x06 or 0x0A or 0x2B))
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments()) is MethodInfo m
                    && m.MetadataToken == MarkPaid.MetadataToken && m.Module == MarkPaid.Module)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
            {
                // not a method token at this offset
            }
        }

        return false;
    }
}
