using NetArchTest.Rules;
using Ta3lom.Store.ArchitectureTests.Support;

namespace Ta3lom.Store.ArchitectureTests;

/// Clean Architecture dependency rule (09 §2 r2, CLAUDE.md). A violation fails the build's test step.
public sealed class LayerTests
{
    [Fact]
    public void Domain_has_no_outward_dependencies() =>
        Types.InAssembly(Assemblies.Domain).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions",
                "Npgsql", "FluentValidation", "Ta3lom.Store.Application", "Ta3lom.Store.Infrastructure",
                "Ta3lom.Store.Contracts", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Fact]
    public void Application_references_Domain_only() =>
        Types.InAssembly(Assemblies.Application).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore",
                "Ta3lom.Store.Contracts", "Ta3lom.Store.Infrastructure", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Fact]
    public void Contracts_are_sealed_and_depend_on_nothing_of_ours()
    {
        Types.InAssembly(Assemblies.Contracts).Should().BeSealed()
            .GetResult().FailingTypeNames.ShouldBeNull();
        Types.InAssembly(Assemblies.Contracts).ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Domain", "Ta3lom.Store.Application", "Ta3lom.Store.Infrastructure", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();
    }

    /// Infrastructure may use ASP.NET Core Data Protection (a standalone library, 09 §5.13) but nothing of the web stack.
    [Fact]
    public void Infrastructure_does_not_reference_Api_or_the_web_stack() =>
        Types.InAssembly(Assemblies.Infrastructure).ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Api", "Ta3lom.Store.Contracts", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Routing", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Authorization")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Fact]
    public void Endpoints_do_not_touch_persistence() =>
        Types.InAssembly(Assemblies.Api).That().ResideInNamespace("Ta3lom.Store.Api.Endpoints").ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Infrastructure.Persistence", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Theory]
    [InlineData("Domain", new string[0])]
    [InlineData("Contracts", new string[0])]
    [InlineData("Application", new[] { "Ta3lom.Store.Domain" })]
    [InlineData("Infrastructure", new[] { "Ta3lom.Store.Application", "Ta3lom.Store.Domain" })]
    public void Compiled_assembly_references_point_inward_only(string layer, string[] allowed)
    {
        var assembly = Assemblies.Production.Single(a => a.GetName().Name == $"Ta3lom.Store.{layer}");

        var ours = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Ta3lom.", StringComparison.Ordinal));

        ours.ShouldBeSubsetOf(allowed);
    }

    [Fact]
    public void No_money_as_floating_point() =>
        MoneyMemberScanner.FindFloatingPointMoney(Assemblies.Domain, Assemblies.Application, Assemblies.Contracts, Assemblies.Api)
            .ShouldBeEmpty();

    [Fact]
    public void Money_scanner_detects_a_violation() =>
        MoneyMemberScanner.Scan(typeof(FloatingPointSample)).ShouldBe(
            [$"{typeof(FloatingPointSample).FullName}.UnitPrice", $"{typeof(FloatingPointSample).FullName}.Discount(amount)"],
            ignoreOrder: true);

    private sealed class FloatingPointSample
    {
        public double UnitPrice { get; set; }

        public int Quantity { get; set; }

        public static void Discount(float amount) => _ = amount;
    }
}
