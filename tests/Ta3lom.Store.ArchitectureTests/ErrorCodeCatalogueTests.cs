using System.Globalization;
using System.Reflection;
using System.Resources;
using Ta3lom.Store.ArchitectureTests.Support;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.ArchitectureTests;

/// Every ErrorCodes constant has a title in Arabic (the neutral resources) and in English, so a problem body is never
/// returned without a localised text.
public sealed class ErrorCodeCatalogueTests
{
    private static readonly ResourceManager Messages = new("Ta3lom.Store.Api.Resources.ErrorMessages", Assemblies.Api);

    private static List<string> Codes() =>
        [.. typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void Every_error_code_has_an_Arabic_and_an_English_title()
    {
        var codes = Codes();
        codes.ShouldNotBeEmpty();

        codes.Where(c => string.IsNullOrWhiteSpace(Messages.GetString(c, CultureInfo.GetCultureInfo("ar")))).ShouldBeEmpty("missing in ErrorMessages.resx");
        codes.Where(c => !HasOwnEnglishTitle(c)).ShouldBeEmpty("missing in ErrorMessages.en.resx");
    }

    [Fact]
    public void Error_codes_are_unique()
    {
        var codes = Codes();
        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Count);
    }

    /// The English satellite must have its own entry; the fallback to the neutral (Arabic) text does not count.
    private static bool HasOwnEnglishTitle(string code) =>
        Messages.GetResourceSet(CultureInfo.GetCultureInfo("en"), createIfNotExists: true, tryParents: false)?.GetString(code) is { Length: > 0 };
}
