# P0-3 — Arabic PDF spike (QuestPDF + IBM Plex Sans Arabic)

**Date:** 30 September 2026 · **Code:** `spikes/P0-3/` (console app, not in `Ta3lom.Store.slnx`; run `dotnet run` in
that folder) · **Output:** `artifacts/spikes/P0-3/invoice-INV-2026-000412.pdf`, `receipt-DR-2026-000187.pdf` (not committed)

## Question (plan 14 §5)

Does QuestPDF shape Arabic, mix RTL/LTR numbers and render tables and the QR correctly with IBM Plex Sans Arabic? Is
the Community licence valid for the association?

## Result: **GO**

| Check | Result |
|---|---|
| Arabic shaping (joining forms, lam-alef ligatures, tashkeel `مُحمّد`, `ّ` shadda) | ✅ correct with IBM Plex Sans Arabic Regular/Bold (HarfBuzz shaping inside QuestPDF) |
| RTL page (`ContentFromRightToLeft`), right-aligned paragraphs, table columns right-to-left | ✅ |
| LTR runs inside RTL text: `T3-26-004187`, `INV-2026-000412`, `2026/09/14`, amounts `350.00` | ✅ as plain runs |
| Phone `+966 55 123 4567` and e-mail inside an Arabic sentence | ❌ the `+` jumps to the end with plain text or LRM marks → ✅ wrapped in Unicode isolates `U+2066 … U+2069` (LRI/PDI) |
| A whole English sentence in an RTL page | ❌ final period moves to the left (paragraph direction RTL; `ContentFromLeftToRight` on the item did not change it) → ✅ wrapped in LRI/PDI |
| Money formatting | ✅ `InvariantCulture` `0.00` / `N2` (Western digits, `.` decimal). ❌ `ar-SA` `N2` gave `1,500,00` → never format money with the Arabic culture |
| Fallback font | ✅ Noto Naskh Arabic renders the same text; both embedded as subsets (`FontFamily(primary, fallback)`) |
| ZATCA phase-1 QR (TLV tags 1–5, base64) | ✅ QRCoder `PngByteQRCode` (no System.Drawing), ECC M, 96 pt; payload decodes to seller/VAT no/timestamp/total/VAT |
| Page footer `صفحة 1 من 1` | ✅ |
| Size / speed | invoice 51 KB, receipt 45 KB; first render ~350 ms (font load), then **~17 ms per invoice** warm |

## Decisions for S-17

1. Packages: `QuestPDF` 2026.9.1, `QRCoder` 1.8.0 (pre-approved list); `QuestPDF.Settings.License = LicenseType.Community`.
   The QuestPDF Community licence is free for organisations under the revenue threshold and for non-profits; the
   association qualifies — **REVIEW** (owner confirms, logged in DECISIONS).
2. Fonts: `Infrastructure/Resources/Fonts/` IBM Plex Sans Arabic (primary) + Noto Naskh Arabic (fallback), both SIL
   OFL 1.1 with their licence files; registered once at start-up with `FontManager.RegisterFontFromStream`.
3. Every mixed-direction value (phone, e-mail, English sentence, URLs) goes through a `Bidi.Ltr(text)` helper that
   wraps it in `U+2066 … U+2069`. References and plain numbers need nothing.
4. Money in documents is always `Money.ToWire()` (invariant, two decimals) plus ` ر.س`; dates `yyyy/MM/dd` invariant.
5. APIs renamed in 2026.9: `Settings.EnableDetailedLayoutErrors`, `FontManager.RegisterFontFromStream`.
6. Documents render from the outbox (`RenderDocuments`), store the PDF as a private `media_asset` and set
   `pdf_asset_id`; generation is fast enough to run inline in the dispatcher.
