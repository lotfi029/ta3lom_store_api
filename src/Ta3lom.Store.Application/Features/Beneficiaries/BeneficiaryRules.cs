using System.Text;
using System.Text.RegularExpressions;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.Beneficiaries;

/// Shared input rules for create, update and CSV import (RUL-51): the same checks, field names and messages everywhere.
public static partial class BeneficiaryRules
{
    public const int NameMax = 200;
    public const int RegionMax = 100;
    public const int NotesMax = 1000;

    public static readonly string[] CsvColumns =
        ["kind", "beneficiary_group", "region", "full_name", "mobile", "guardian_name", "organisation_name", "eligibility_notes"];

    /// Validates and normalises; `errors` uses the camelCase field names of `BeneficiaryInput`.
    public static BeneficiaryDetails? TryParse(BeneficiaryInput input, IPseudonymizer hasher, out Dictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hasher);
        errors = new Dictionary<string, string>(StringComparer.Ordinal);
        BeneficiaryKind kind = default;
        if (string.IsNullOrWhiteSpace(input.Kind) || !EnumWire.TryParse(input.Kind.Trim(), out kind))
        {
            errors["kind"] = "must be student|teacher|parent|school|library";
        }

        var group = input.BeneficiaryGroup?.Trim();
        if (string.IsNullOrEmpty(group) || !GroupKey().IsMatch(group))
        {
            errors["beneficiaryGroup"] = "must be a group key (a-z, 0-9, _; up to 64)";
        }

        var region = Clean(input.Region);
        if (region is null || region.Length > RegionMax)
        {
            errors["region"] = $"is required (up to {RegionMax} characters)";
        }

        var name = Clean(input.FullName);
        if (name is null || name.Length < 2 || name.Length > NameMax)
        {
            errors["fullName"] = $"is required (2–{NameMax} characters)";
        }

        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(input.Mobile) && !SaudiMobile.TryNormalize(input.Mobile.Trim(), out mobile))
        {
            errors["mobile"] = "must be a Saudi mobile (05XXXXXXXX)";
        }

        var guardian = Clean(input.GuardianName);
        if (guardian is { Length: > NameMax })
        {
            errors["guardianName"] = $"up to {NameMax} characters";
        }

        var organisation = Clean(input.OrganisationName);
        if (organisation is { Length: > NameMax })
        {
            errors["organisationName"] = $"up to {NameMax} characters";
        }

        var notes = Clean(input.EligibilityNotes);
        if (notes is { Length: > NotesMax })
        {
            errors["eligibilityNotes"] = $"up to {NotesMax} characters";
        }

        return errors.Count > 0
            ? null
            : new BeneficiaryDetails(kind, group!, region!, name!, mobile, mobile is null ? null : hasher.HashMobile(mobile), guardian, organisation, notes);
    }

    /// `هيا العنزي` → `ه••• ا•••` (first letter of each word).
    public static string MaskName(string fullName) =>
        string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => string.Concat(w.AsSpan(0, 1), "•••")));

    public static BeneficiaryRow ToRow(Beneficiary b) =>
        new(b.Id, b.Code, b.Kind.ToWire(), b.BeneficiaryGroup, b.Region, b.OrganisationName, MaskName(b.FullName),
            b.Mobile is null ? null : SaudiMobile.Mask(b.Mobile), b.EligibilityStatus.ToWire(), b.ZakatEligible, b.CustomerId is not null,
            b.VerifiedBy, b.VerifiedAt);

    /// UTF-8 with BOM (Excel opens Arabic correctly), header row only.
    public static byte[] Template() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join(',', CsvColumns) + "\r\n")];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupKey();
}
