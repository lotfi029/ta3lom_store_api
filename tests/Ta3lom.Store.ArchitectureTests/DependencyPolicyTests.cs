using System.Xml.Linq;
using Ta3lom.Store.ArchitectureTests.Support;

namespace Ta3lom.Store.ArchitectureTests;

/// No MediatR, AutoMapper or FluentAssertions (commercial licences).
public sealed class DependencyPolicyTests
{
    private static readonly string[] Forbidden = ["MediatR", "AutoMapper", "FluentAssertions"];

    [Fact]
    public void Forbidden_libraries_are_not_referenced_by_any_assembly()
    {
        var referenced = Assemblies.Production.Concat(Assemblies.Tests)
            .SelectMany(a => a.GetReferencedAssemblies())
            .Select(a => a.Name!);

        referenced.Where(n => Forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ShouldBeEmpty();
    }

    [Fact]
    public void Forbidden_libraries_are_not_in_central_package_management()
    {
        var packages = XDocument.Load(Assemblies.Doc("Directory.Packages.props"))
            .Descendants("PackageVersion").Select(e => (string)e.Attribute("Include")!).ToList();

        packages.ShouldNotBeEmpty();
        packages.Where(p => Forbidden.Any(f => p.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ShouldBeEmpty();
    }
}
