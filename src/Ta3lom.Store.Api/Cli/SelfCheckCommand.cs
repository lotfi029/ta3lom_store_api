using Ta3lom.Store.Infrastructure.Services.Receipts;

namespace Ta3lom.Store.Api.Cli;

/// `dotnet Ta3lom.Store.Api.dll self-check`: renders a sample receipt PDF (QuestPDF native libraries, embedded Arabic
/// font) without the web host, database or secrets. Exit 0 when the PDF was produced. Run by CI and the deploy on the
/// built image.
internal static class SelfCheckCommand
{
    public const string Verb = "self-check";

    public static int Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var bytes = ReceiptSelfCheck.RenderSampleBytes();
        output.WriteLine($"self-check: receipt PDF rendered ({bytes} bytes)");
        return bytes > 1000 ? 0 : 1;
    }
}
