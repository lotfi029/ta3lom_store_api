namespace Ta3lom.Store.Domain.ValueObjects;

/// Saudi mobile numbers: accepted as `05XXXXXXXX`, `5XXXXXXXX`, `9665XXXXXXXX` or
/// `+9665XXXXXXXX` (spaces and dashes ignored), stored as E.164 `+9665XXXXXXXX`, shown masked `05•••••567`.
public static class SaudiMobile
{
    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Span<char> digits = stackalloc char[16];
        var n = 0;
        foreach (var ch in input.Trim())
        {
            if (ch is ' ' or '-')
            {
                continue;
            }

            if (ch == '+' && n == 0)
            {
                continue;
            }

            if (!char.IsAsciiDigit(ch) || n == digits.Length)
            {
                return false;
            }

            digits[n++] = ch;
        }

        var d = new string(digits[..n]);
        var national = d switch
        {
            { Length: 10 } when d.StartsWith("05", StringComparison.Ordinal) => d[1..],
            { Length: 9 } when d.StartsWith('5') => d,
            { Length: 12 } when d.StartsWith("9665", StringComparison.Ordinal) => d[3..],
            { Length: 14 } when d.StartsWith("009665", StringComparison.Ordinal) => d[5..],
            _ => null,
        };
        if (national is null)
        {
            return false;
        }

        e164 = "+966" + national;
        return true;
    }

    public static bool IsValid(string? input) => TryNormalize(input, out _);

    /// `+966551234567` → `05•••••567` (display only; never used as a key).
    public static string Mask(string? e164)
    {
        if (!TryNormalize(e164, out var normalized))
        {
            return "•••";
        }

        var local = "0" + normalized[4..];
        return string.Concat(local.AsSpan(0, 2), "•••••", local.AsSpan(local.Length - 3));
    }

    /// `+966551234567` → `0551234567`.
    public static string ToLocal(string e164) => TryNormalize(e164, out var n) ? "0" + n[4..] : e164;
}
