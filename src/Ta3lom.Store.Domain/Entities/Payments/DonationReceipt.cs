using System.Globalization;

namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `donation_receipt` (F-D4): DR (one per paid order) and DRC (voids the DR on a full refund), numbered without
/// gaps per series and Riyadh year (`UNIQUE (series, year, number)`, numbers from `document_counter` under a row lock in
/// the settling transaction). The donor name and the lines are snapshots; the PDF lives in private storage.
public sealed class DonationReceipt : Entity
{
    private DonationReceipt(Guid id) : base(id)
    {
    }

    private DonationReceipt()
    {
    }

    public Guid OrderId { get; private set; }

    public ReceiptSeries Series { get; private set; }

    public int Year { get; private set; }

    public int Number { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public Money Amount { get; private set; }

    public string DonorNameSnapshot { get; private set; } = null!;

    /// JSON array of the order lines at issue time (title ar/en, option label, quantity, amount).
    public string LinesSnapshot { get; private set; } = "[]";

    public string? PdfStorageKey { get; private set; }

    /// On a DR: the DRC that voided it. On a DRC: null.
    public Guid? VoidedByReceiptId { get; private set; }

    /// On a DRC: the DR it voids.
    public Guid? VoidsReceiptId { get; private set; }

    /// `DR-2026-000001`.
    public string DisplayNumber => FormatNumber(Series, Year, Number);

    public static string FormatNumber(ReceiptSeries series, int year, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{series.ToWire()}-{year:D4}-{number:D6}");

    public static DonationReceipt IssueDonation(Guid orderId, int year, int number, Money amount, string donorName,
        string linesSnapshot, DateTimeOffset now) =>
        new(Guid.CreateVersion7())
        {
            OrderId = Guard.NotEmpty(orderId),
            Series = ReceiptSeries.Dr,
            Year = year,
            Number = number > 0 ? number : throw new DomainException("receipt.number_invalid"),
            Amount = amount,
            DonorNameSnapshot = Guard.NotEmpty(donorName),
            LinesSnapshot = Guard.NotEmpty(linesSnapshot),
            IssuedAt = now,
        };

    /// DRC for a full refund: voids `donation` (the order's DR).
    public static DonationReceipt IssueCancellation(DonationReceipt donation, int year, int number, Money amount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(donation);
        Guard.Against(donation.Series != ReceiptSeries.Dr, "receipt.voids_dr_only");
        Guard.Against(donation.VoidedByReceiptId is not null, "receipt.already_voided");
        var drc = new DonationReceipt(Guid.CreateVersion7())
        {
            OrderId = donation.OrderId,
            Series = ReceiptSeries.Drc,
            Year = year,
            Number = number > 0 ? number : throw new DomainException("receipt.number_invalid"),
            Amount = amount,
            DonorNameSnapshot = donation.DonorNameSnapshot,
            LinesSnapshot = donation.LinesSnapshot,
            VoidsReceiptId = donation.Id,
            IssuedAt = now,
        };
        donation.VoidedByReceiptId = drc.Id;
        // Review 04 R-1: the DR's cached PDF predates the void; it is re-rendered (with the «ملغى» banner) on the next read.
        donation.PdfStorageKey = null;
        return drc;
    }

    public void StorePdf(string storageKey) => PdfStorageKey = Guard.NotEmpty(storageKey);

    /// D-14: deleting a donor anonymises the receipt's name snapshot (the receipt, its number and amount stay). The stored
    /// PDF carried the name: its key is cleared and returned so the caller deletes the file (re-rendered on demand).
    public string? AnonymiseDonor()
    {
        DonorNameSnapshot = Donations.Order.AnonymisedName;
        var pdf = PdfStorageKey;
        PdfStorageKey = null;
        return pdf;
    }
}
