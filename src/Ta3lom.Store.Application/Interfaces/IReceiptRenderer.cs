namespace Ta3lom.Store.Application.Interfaces;

/// Renders a donation receipt (DR) or its cancellation (DRC) to PDF (F-D4; QuestPDF in Infrastructure). Arabic first,
/// right-to-left, with the font embedded.
public interface IReceiptRenderer
{
    byte[] Render(ReceiptDocument document);
}

/// Everything the PDF shows; strings are ready to print (money as `"350.00"`, dates already in Riyadh).
public sealed record ReceiptDocument(
    string Number,
    bool IsCancellation,
    string? VoidsNumber,
    string OrderReference,
    string IssuedGregorian,
    string IssuedHijri,
    string DonorName,
    IReadOnlyList<ReceiptDocumentLine> Lines,
    string Total,
    string TotalInWords,
    string OrgNameAr,
    string? OrgNameEn,
    string? LicenceNo,
    string? VatNoteAr,
    string? VatNoteEn,
    byte[]? SignatureImage,
    string? VoidedByNumber = null);

public sealed record ReceiptDocumentLine(string Description, int Quantity, string UnitAmount, string Amount);
