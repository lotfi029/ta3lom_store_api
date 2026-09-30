using System.Globalization;
using System.Reflection;
using System.Resources;
using Ta3lom.Store.Api.Http;
using Ta3lom.Store.Application.Abstractions;

namespace Ta3lom.Store.Api.IntegrationTests.Contract;

/// A2: every ErrorCodes constant has a non-empty title in Arabic (neutral) and English.
public sealed class ErrorMessagesCompletenessTests
{
    private static readonly ResourceManager Resources =
        new("Ta3lom.Store.Api.Resources.ErrorMessages", typeof(ErrorMessages).Assembly);

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var code in typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!))
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Arabic_title_exists(string code) =>
        OwnEntry("ar", code).ShouldNotBeNullOrWhiteSpace();

    [Theory]
    [MemberData(nameof(Codes))]
    public void English_title_exists_and_is_not_the_Arabic_fallback(string code)
    {
        var en = OwnEntry("en", code);

        en.ShouldNotBeNullOrWhiteSpace();
        en.ShouldNotBe(OwnEntry("ar", code));
    }

    [Fact]
    public void Arabic_is_the_neutral_resource_language() =>
        typeof(ErrorMessages).Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>()!.CultureName.ShouldBe("ar");

    /// The entry of exactly this culture's resource set, without falling back to the parent.
    private static string? OwnEntry(string culture, string code) =>
        Resources.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)?.GetString(code);
}
