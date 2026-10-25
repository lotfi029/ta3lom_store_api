using NetArchTest.Rules;
using Ta3lom.Store.ArchitectureTests.Support;

namespace Ta3lom.Store.ArchitectureTests;

/// DMS dependency rule with the store's (K) exceptions (Architecture-alignment-DMS §3–§4, CLAUDE.md):
/// SharedKernel ← Domain ← Application ← Infrastructure ← Api. A violation fails the build's test step.
public sealed class LayerTests
{
    [Fact]
    public void SharedKernel_depends_on_nothing_of_ours() =>
        Types.InAssembly(Assemblies.SharedKernel).ShouldNot()
            .HaveDependencyOnAny("Microsoft", "Npgsql", "FluentValidation", "Ta3lom.Store.Domain", "Ta3lom.Store.Application",
                "Ta3lom.Store.Infrastructure", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();

    /// No EF in Domain; only the DI abstractions, and only for AddDomain().
    [Fact]
    public void Domain_has_no_outward_dependencies() =>
        Types.InAssembly(Assemblies.Domain).That().DoNotHaveName("DependencyInjection").ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions",
                "Npgsql", "FluentValidation", "Mapster", "Ta3lom.Store.Application", "Ta3lom.Store.Infrastructure", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Fact]
    public void Domain_registration_uses_only_the_DI_abstractions() =>
        Assemblies.Domain.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("Microsoft.", StringComparison.Ordinal))
            .ShouldBeSubsetOf(["Microsoft.Extensions.DependencyInjection.Abstractions"]);

    /// EF Core abstractions for IApplicationDbContext reads only; never Npgsql, Relational or ASP.NET Core.
    [Fact]
    public void Application_references_Domain_and_EF_abstractions_only()
    {
        Types.InAssembly(Assemblies.Application).ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Microsoft.AspNetCore", "Ta3lom.Store.Infrastructure", "Ta3lom.Store.Api")
            .GetResult().FailingTypeNames.ShouldBeNull();
        Assemblies.Application.GetReferencedAssemblies().Select(a => a.Name!)
            .ShouldNotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n == "Microsoft.EntityFrameworkCore.Relational");
    }

    /// DTOs live in Application/DTOs; they stay sealed and never expose entities.
    [Fact]
    public void Application_DTOs_are_sealed_and_do_not_expose_entities()
    {
        var dtos = Types.InAssembly(Assemblies.Application).That().ResideInNamespaceStartingWith("Ta3lom.Store.Application.DTOs");
        dtos.Should().BeSealed().GetResult().FailingTypeNames.ShouldBeNull();
        Types.InAssembly(Assemblies.Application).That().ResideInNamespaceStartingWith("Ta3lom.Store.Application.DTOs").ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Domain.Entities")
            .GetResult().FailingTypeNames.ShouldBeNull();
    }

    /// Request/response records live in Application/DTOs, not in the Api.
    [Fact]
    public void Api_defines_no_DTOs() =>
        Assemblies.Api.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Ta3lom.Store.Api", StringComparison.Ordinal) == true
                        && (t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Response", StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ShouldBeEmpty();

    /// Infrastructure may use ASP.NET Core Data Protection (a standalone library) but nothing of the web stack.
    [Fact]
    public void Infrastructure_does_not_reference_Api_or_the_web_stack() =>
        Types.InAssembly(Assemblies.Infrastructure).ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Api", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Routing", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Authorization")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Fact]
    public void Endpoints_do_not_touch_persistence() =>
        Types.InAssembly(Assemblies.Api).That().ResideInNamespace("Ta3lom.Store.Api.Endpoints").ShouldNot()
            .HaveDependencyOnAny("Ta3lom.Store.Infrastructure.Persistence", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult().FailingTypeNames.ShouldBeNull();

    [Theory]
    [InlineData("SharedKernel", new string[0])]
    [InlineData("Domain", new[] { "Ta3lom.Store.SharedKernel" })]
    [InlineData("Application", new[] { "Ta3lom.Store.Domain", "Ta3lom.Store.SharedKernel" })]
    [InlineData("Infrastructure", new[] { "Ta3lom.Store.Application", "Ta3lom.Store.Domain", "Ta3lom.Store.SharedKernel" })]
    public void Compiled_assembly_references_point_inward_only(string layer, string[] allowed)
    {
        var assembly = Assemblies.Production.Single(a => a.GetName().Name == $"Ta3lom.Store.{layer}");

        var ours = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Ta3lom.", StringComparison.Ordinal));

        ours.ShouldBeSubsetOf(allowed);
    }

    [Fact]
    public void No_money_as_floating_point() =>
        MoneyMemberScanner.FindFloatingPointMoney(Assemblies.SharedKernel, Assemblies.Domain, Assemblies.Application, Assemblies.Api)
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
