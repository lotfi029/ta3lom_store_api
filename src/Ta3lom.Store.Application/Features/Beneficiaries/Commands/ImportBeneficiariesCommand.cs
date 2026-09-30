using System.Text;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.Beneficiaries.Commands;

/// `POST /v1/admin/beneficiaries/import` (#107, FR-S52): UTF-8 CSV with the template columns. Every row is checked with the
/// same rules as the form, plus duplicate mobiles against the register and within the file (`mobile_hash`). `dryRun`
/// (default) only reports. A real import is atomic per file: any invalid row → 422 `beneficiary.import_invalid` with the
/// report and nothing saved; otherwise every row is created `pending` in one transaction (approval is separate, RUL-51).
public sealed record ImportBeneficiariesCommand(Stream Content, long Length, bool DryRun) : ICommand<ImportReport>;

internal sealed class ImportBeneficiariesCommandHandler(IApplicationDbContext db, IBeneficiaryRepository beneficiaries, IPseudonymizer hasher,
    ICurrentActor actor, IUnitOfWork uow, IClock clock) : ICommandHandler<ImportBeneficiariesCommand, ImportReport>
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 5000;

    public async Task<Result<ImportReport>> HandleAsync(ImportBeneficiariesCommand command, CancellationToken ct = default)
    {
        if (command.Length is <= 0 or > MaxBytes)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["file"] = [$"must be a CSV file of up to {MaxBytes / 1024 / 1024} MB"] });
        }

        string text;
        try
        {
            using var reader = new StreamReader(command.Content, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(ct);
        }
        catch (DecoderFallbackException)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["file"] = ["must be UTF-8 text"] });
        }

        var records = CsvReader.Read(text);
        if (records.Count == 0 || !records[0].Select(h => h.Trim().ToLowerInvariant()).SequenceEqual(BeneficiaryRules.CsvColumns))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["file"] = ["the header must be: " + string.Join(',', BeneficiaryRules.CsvColumns)] });
        }

        var data = records.Skip(1).Select((fields, i) => (Row: i + 2, Fields: fields)).Where(r => r.Fields.Any(f => f.Trim().Length > 0)).ToList();
        if (data.Count > MaxRows)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["file"] = [$"up to {MaxRows} rows per file"] });
        }

        var errors = new List<ImportRowError>();
        var valid = new List<BeneficiaryDetails>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<(int Row, BeneficiaryDetails Details)>();
        foreach (var (row, f) in data)
        {
            if (f.Length != BeneficiaryRules.CsvColumns.Length)
            {
                errors.Add(new ImportRowError(row, ErrorCodes.ValidationFailed, "row", $"expected {BeneficiaryRules.CsvColumns.Length} columns, found {f.Length}"));
                continue;
            }

            var input = new BeneficiaryInput(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7]);
            var details = BeneficiaryRules.TryParse(input, hasher, out var fieldErrors);
            if (details is null)
            {
                errors.AddRange(fieldErrors.Select(e => new ImportRowError(row, ErrorCodes.ValidationFailed, e.Key, e.Value)));
                continue;
            }

            if (details.MobileHash is { } hash && !seen.Add(hash))
            {
                errors.Add(new ImportRowError(row, ErrorCodes.BeneficiaryMobileExists, "mobile", "repeated in this file"));
                continue;
            }

            parsed.Add((row, details));
        }

        var hashes = parsed.Where(p => p.Details.MobileHash is not null).Select(p => p.Details.MobileHash!).ToList();
        var existing = hashes.Count == 0
            ? []
            : (await db.Beneficiaries.AsNoTracking().Where(b => b.MobileHash != null && hashes.Contains(b.MobileHash)).Select(b => b.MobileHash!).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
        foreach (var (row, details) in parsed)
        {
            if (details.MobileHash is { } hash && existing.Contains(hash))
            {
                errors.Add(new ImportRowError(row, ErrorCodes.BeneficiaryMobileExists, "mobile", null));
            }
            else
            {
                valid.Add(details);
            }
        }

        errors.Sort((a, b) => a.Row.CompareTo(b.Row));
        var report = new ImportReport(command.DryRun, data.Count, valid.Count, 0, errors);
        if (command.DryRun)
        {
            return report;
        }

        if (errors.Count > 0 || valid.Count == 0)
        {
            return BeneficiaryErrors.ImportInvalid(report);
        }

        var now = clock.UtcNow;
        var by = actor.Name ?? "staff";
        await uow.ExecuteInTransactionAsync(async token =>
        {
            foreach (var details in valid)
            {
                beneficiaries.Add(Beneficiary.Register(await beneficiaries.NextCodeAsync(token), details, by, now));
            }

            await uow.SaveChangesAsync(token);
        }, ct);
        return report with { Created = valid.Count };
    }
}
