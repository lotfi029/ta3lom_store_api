using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Payments;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Receipts;

/// A receipt's PDF: rendered ONCE (outbox `ReceiptIssued`, or the first download if the outbox has not run yet), stored
/// in PRIVATE file storage (`receipts/{year}/{number}.pdf`, never served publicly), then read back. The texts come from
/// the `receipt_*` settings; the donor name and lines are the receipt's snapshots. A DR voided by a DRC renders with the
/// banner «ملغى — DRC-…» (review 04 R-1; issuing the DRC drops the DR's cached PDF).
public sealed class ReceiptFiles(IApplicationDbContext db, IReceiptRepository receipts, IReceiptRenderer renderer, IFileStorage files,
    IUnitOfWork uow, IClock clock)
{
    public async Task<ReceiptFile> GetOrRenderAsync(Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await receipts.GetByIdAsync(receiptId, ct) ?? throw new InvalidOperationException($"Receipt {receiptId} not found.");
        var name = $"{receipt.DisplayNumber}.pdf";
        if (receipt.PdfStorageKey is { } stored && await files.OpenReadAsync(stored, isPublic: false, ct) is { } stream)
        {
            await using (stream)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                return new ReceiptFile(buffer.ToArray(), name);
            }
        }

        var pdf = renderer.Render(await DocumentAsync(receipt, ct));
        var key = $"receipts/{receipt.Year.ToString(CultureInfo.InvariantCulture)}/{name}";
        using (var content = new MemoryStream(pdf))
        {
            await files.SaveAsync(key, isPublic: false, content, ct);
        }

        receipt.StorePdf(key);
        await uow.SaveChangesAsync(ct);
        return new ReceiptFile(pdf, name);
    }

    private async Task<ReceiptDocument> DocumentAsync(DonationReceipt r, CancellationToken ct)
    {
        var reference = await db.Orders.AsNoTracking().Where(o => o.Id == r.OrderId).Select(o => o.Reference).FirstAsync(ct);
        var voids = r.VoidsReceiptId is { } dr
            ? await db.DonationReceipts.AsNoTracking().Where(x => x.Id == dr).Select(x => new { x.Series, x.Year, x.Number }).FirstAsync(ct)
            : null;
        var voidedBy = r.VoidedByReceiptId is { } drc
            ? await db.DonationReceipts.AsNoTracking().Where(x => x.Id == drc).Select(x => new { x.Series, x.Year, x.Number }).FirstAsync(ct)
            : null;
        var keys = new[] { SettingKeys.ReceiptOrgNameAr, SettingKeys.ReceiptOrgNameEn, SettingKeys.ReceiptLicenceNo, SettingKeys.ReceiptVatNoteAr, SettingKeys.ReceiptVatNoteEn, SettingKeys.ReceiptSignatureAssetId };
        var settings = await db.Settings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => Text(s.Value), ct);
        byte[]? signature = null;
        if (settings.GetValueOrDefault(SettingKeys.ReceiptSignatureAssetId) is { } assetId && Guid.TryParse(assetId, out var id)
            && await db.MediaAssets.AsNoTracking().Where(m => m.Id == id).Select(m => m.StorageKey).FirstOrDefaultAsync(ct) is { } assetKey
            && await files.OpenReadAsync(assetKey, isPublic: true, ct) is { } image)
        {
            await using (image)
            {
                using var buffer = new MemoryStream();
                await image.CopyToAsync(buffer, ct);
                signature = buffer.ToArray();
            }
        }

        var lines = JsonSerializer.Deserialize<List<ReceiptLine>>(r.LinesSnapshot, ReceiptLine.Json) ?? [];
        var issued = clock.ToRiyadh(r.IssuedAt);
        var hijri = new CultureInfo("ar-SA") { DateTimeFormat = { Calendar = new UmAlQuraCalendar() } };
        hijri.NumberFormat.DigitSubstitution = DigitShapes.None;
        return new ReceiptDocument(
            r.DisplayNumber,
            r.Series == ReceiptSeries.Drc,
            voids is null ? null : DonationReceipt.FormatNumber(voids.Series, voids.Year, voids.Number),
            reference,
            issued.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Western(issued.DateTime.ToString("dd/MM/yyyy", hijri)),
            r.DonorNameSnapshot,
            [.. lines.Select(l => new ReceiptDocumentLine(Describe(l), l.Quantity, l.UnitAmount, l.Amount))],
            r.Amount.ToWire(),
            ArabicAmountWords.Of(r.Amount),
            settings.GetValueOrDefault(SettingKeys.ReceiptOrgNameAr) ?? "جمعية التعلم الإلكتروني",
            settings.GetValueOrDefault(SettingKeys.ReceiptOrgNameEn),
            settings.GetValueOrDefault(SettingKeys.ReceiptLicenceNo),
            settings.GetValueOrDefault(SettingKeys.ReceiptVatNoteAr),
            settings.GetValueOrDefault(SettingKeys.ReceiptVatNoteEn),
            signature,
            voidedBy is null ? null : DonationReceipt.FormatNumber(voidedBy.Series, voidedBy.Year, voidedBy.Number));
    }

    private static string Describe(ReceiptLine l) => l.OptionLabelAr is { Length: > 0 } option ? $"{l.TitleAr} — {option}" : l.TitleAr;

    /// Western digits everywhere (product rule), even if the culture would print Arabic-Indic ones.
    private static string Western(string s) => new([.. s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)]);

    private static string? Text(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.String && doc.RootElement.GetString() is { Length: > 0 } s ? s.Trim() : null;
    }
}

public sealed record ReceiptFile(byte[] Pdf, string FileName);
