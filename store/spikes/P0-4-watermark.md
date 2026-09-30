# P0-4 — Watermark spike (PdfSharp on a 300-page book)

**Date:** 30 September 2026 · **Code:** `spikes/P0-4/` (console app, not in `Ta3lom.Store.slnx`; `dotnet run -c Release`)
· **Output:** `artifacts/spikes/P0-4/` (not committed)

## Question (plan 14 §5)

Can PdfSharp stamp an Arabic name on every page of a 300-page book fast enough? Timing and size, go/no-go.

## Result: **GO — with the stamp rendered by QuestPDF and drawn by PdfSharp**

| Variant | Time for 301 pages (cold / warm / warm) | Output size (input 1 051 KB) | Arabic |
|---|---|---|---|
| QuestPDF renders the stamp once (IBM Plex Sans Arabic, RTL, HarfBuzz shaping) → PdfSharp imports it as a form XObject (`XPdfForm`) and draws it on every page | 307 / 193 / 222 ms | 869 KB | ✅ joined letters, correct order, tashkeel, LTR runs isolated |
| PdfSharp `DrawString` with an IBM Plex font resolver | 262 / 259 / 243 ms | 891 KB | ❌ letters unjoined (no shaping) — unusable for names |

The stamp text: `نسخة خاصة بـ {name} — ⁦05•••••567⁩ — T3-26-004187 — لا يجوز نشرها`, grey 8 pt, bottom centre of every
page, plus `Info.Subject = Licensed to {reference}`. Text extraction finds the stamp on every page (so it survives copy/paste
and search, and T-8 can assert it).

## Decisions for S-20

1. Package: `PDFsharp` 6.2.4 (MIT, pre-approved list) for page-level stamping only; all text is rendered by QuestPDF.
2. `PdfSharpWatermarker`: render the stamp page with QuestPDF at the source page size, import once as `XPdfForm`, draw it on
   each page (`XGraphicsPdfPageOptions.Append`); different page sizes → one form per size.
3. ~0.2 s for 300 pages → the first download of a grant may stamp inline; the result is **cached per grant** as a private
   file (`downloads/{grantId}.pdf`), later downloads stream the cached file. The cache is dropped when a grant is reset or revoked.
4. Masked mobile (`05•••••567`) and the order reference identify the buyer without printing the full number (PDPL).
