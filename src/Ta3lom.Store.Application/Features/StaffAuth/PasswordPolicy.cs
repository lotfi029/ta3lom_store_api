using System.Text;

namespace Ta3lom.Store.Application.Features.StaffAuth;

/// D-12: a staff password has at least 12 characters and is not on the common-password list.
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    private static readonly Lazy<HashSet<string>> Common = new(Load);

    /// The reasons a password is refused (`too_short`, `too_long`, `common`), or an empty list.
    public static IReadOnlyList<string> Check(string password)
    {
        List<string> reasons = [];
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            reasons.Add("too_short");
        }

        if (password is { Length: > MaxLength })
        {
            reasons.Add("too_long");
        }

        if (!string.IsNullOrEmpty(password) && (Common.Value.Contains(password.Trim().ToLowerInvariant()) || password.Distinct().Count() == 1))
        {
            reasons.Add("common");
        }

        return reasons;
    }

    private static HashSet<string> Load()
    {
        using var stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream("StaffAuth.common-passwords.txt")
                           ?? throw new InvalidOperationException("common-passwords resource missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return [.. reader.ReadToEnd().Split('\n').Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
    }
}
