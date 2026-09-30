using System.Globalization;
using System.Resources;

namespace Ta3lom.Store.Api.Http;

/// Localised ProblemDetails text, keyed by error code (`{code}` = title, `{code}.detail` = detail).
/// Arabic is the neutral language (Resources/ErrorMessages.resx); English is ErrorMessages.en.resx.
public static class ErrorMessages
{
    private static readonly ResourceManager Resources =
        new("Ta3lom.Store.Api.Resources.ErrorMessages", typeof(ErrorMessages).Assembly);

    public static CultureInfo Arabic { get; } = CultureInfo.GetCultureInfo("ar");

    public static CultureInfo English { get; } = CultureInfo.GetCultureInfo("en");

    public static string? Title(string code, CultureInfo culture) => Get(code, culture);

    public static string? Detail(string code, CultureInfo culture) => Get(code + ".detail", culture);

    private static string? Get(string key, CultureInfo culture)
    {
        var value = Resources.GetString(key, culture);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
