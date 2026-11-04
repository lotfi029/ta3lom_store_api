using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Receipts;

/// `self-check` CLI verb (deploy smoke, F4): renders a sample Arabic receipt with the embedded font, so a broken native
/// PDF stack in the image fails the deploy instead of the first paid order.
public static class ReceiptSelfCheck
{
    public static int RenderSampleBytes() => new QuestPdfReceiptRenderer().Render(new ReceiptDocument(
        "DR-2000-000001", false, null, "T3-00-000000", "01/01/2000", "24/09/1420", "فحص",
        [new ReceiptDocumentLine("فحص الإيصال", 1, "1.00", "1.00")], "1.00", "فقط ريال واحد لا غير",
        "جمعية التعلم الإلكتروني", "E-Learning Association", null, null, null, null)).Length;
}
