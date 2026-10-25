using System.Text.RegularExpressions;

namespace Ta3lom.Store.Domain.ValueObjects;

/// URL slugs (D-08): lower-case ASCII letters, digits and hyphens. Projects allow 2–80 characters, categories 2–60.
public static partial class Slug
{
    public const int ProjectMax = 80;
    public const int CategoryMax = 60;

    public static bool IsValidProject(string? slug) => slug is { Length: >= 2 and <= ProjectMax } && Pattern().IsMatch(slug);

    public static bool IsValidCategory(string? slug) => slug is { Length: >= 2 and <= CategoryMax } && Pattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
