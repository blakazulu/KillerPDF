# Roadmap — Next Features (design specs for review)

**Status:** draft for review (no code yet)
**Date:** 2026-06-30
**Source:** inferred from upstream KillerPDF roadmap signals; grounded in Scalpel's current code (`Services/PdfSigningService.cs`, `Services/OcrService.cs`, `Services/PdfRasterTools.cs`).

Ordered smallest/lowest-risk first; **cross-platform is last** and is a different class of effort. Effort is a rough t-shirt size, not a commitment.

---

## 1. Region OCR — "OCR a selected area to clipboard"  · **S**

**Goal:** Let the user drag a rectangle on the page and copy just that region's recognized text to the clipboard (the one OCR feature upstream has that Scalpel deferred).

**Design.** Reuse the existing rubber-band selection (`_selectStart` / `_selectRect` in `MainWindow.CanvasInteraction.cs`). Add an "OCR Region" entry to the Tools/OCR menu that arms a region-select mode; on mouse-up, map the canvas rect → page-point rect → **crop the rendered page bitmap** to that rect, and OCR the crop with the existing `IOcrEngine.Recognize(imageBytes, wPt, hPt)`. Join words with `OcrTextJoiner` and put the text on the clipboard with a status toast.

**Components.**
- `OcrService`: add `string RecognizeRegionText(IPageRasterizer source, IOcrEngine engine, int pageIndex, Rect regionPt)` — render the page, crop the bitmap to `regionPt` (ImageSharp, as `WatermarkService` already does pixel work), call `Recognize`, return `OcrTextJoiner.Join(...)`. **Pure/testable** with the existing fakes (feed a fake rasterizer + engine; assert the crop bounds passed through).
- `MainWindow`: a region-arm flag + reuse of `_selectRect`; on commit, call the service on a background thread (with the same `EnsureOcrReady()` path), set clipboard.
- Strings (`Str_Ocr_Region*`) ×9, changelog.

**Scope/risk.** Low. The crop math (canvas→page→pixel) is the only fiddly part; covered by a unit test on the pure helper. No new dependency.

**Effort:** **S** (≈ the size of one of this session's Tools features).

---

## 2. Sign from the Windows certificate store (picker)  · **S–M**

**Goal:** Sign with a certificate already installed in Windows (smart cards, enterprise-issued certs), not only a `.pfx` file. Upstream has a `WindowsCertificateStore` + `ICertificateProvider` abstraction for exactly this.

**Design.** Introduce a tiny provider seam mirroring upstream so the signer is source-agnostic:
- `interface ICertificateProvider { X509Certificate2 GetSigner(out X509Certificate2[] chain); }`
- `PfxFileCertificateProvider` (wraps today's `.pfx` + password path) and `WindowsCertificateProvider` (reads `StoreName.My` / `CurrentUser`, filters to certs with a private key + digital-signature key-usage).
- `PdfSigningService.SignBytes(...)` already takes an `X509Certificate2` + chain — **no change to the crypto core**; only the *acquisition* changes.
- UI: the Tools → "Digitally Sign" flow gets a first step — "From file…" vs "From Windows certificate store…". The store path shows a picker dialog (a small themed list of `subject · issuer · expiry`, like `ColorPickerDialog`'s construction). `X509Certificate2UI.SelectFromCollection` is the zero-effort option but isn't themed; a small custom list keeps the Clinical look.

**Components.** `Services/Signing/` (new folder): the interface + two providers; a `CertificatePickerDialog`; wire into `ToolsSign_Click`. Guard the Windows-store provider behind an OS check (sets up #7). Strings ×9, changelog.

**Scope/risk.** Low–medium. Crypto unchanged (already proven). Risk is UI plumbing + correctly filtering signing-capable certs. Smart-card PIN prompts are handled by Windows/CSP automatically.

**Effort:** **S–M.**

---

## 3. RFC-3161 trusted timestamp  · **M**

**Goal:** Embed a trusted timestamp token so the signature proves *when* it was signed and stays verifiable after the signer cert expires — the explicit next step in upstream's signing comments.

**Design.** A timestamp is an **unsigned attribute** (`signatureTimeStampToken`, OID 1.2.840.113549.1.9.16.2.14) added to the CMS `SignerInfo` *after* the signature is computed. Flow inside `BuildDetachedCms` (or a post-step):
1. Compute the signature (as today).
2. Hash the signature bytes (SHA-256), build an RFC-3161 `TimeStampRequest` (BouncyCastle `TimeStampRequestGenerator`), POST it to a TSA URL over HTTP (`application/timestamp-query`).
3. Take the returned `TimeStampToken`, attach it as an unsigned attribute to the `SignerInformation` (BouncyCastle `SignerInformation.ReplaceUnsignedAttributes`).
4. Re-encode the CMS (DER) and embed as before.

BouncyCastle (already a dependency) has the full TSP stack — **no new dependency**. Need a default TSA URL (e.g. a free one like DigiCert's) plus a setting to override it. The 16 KB `/Contents` placeholder must grow (timestamp tokens add 4–8 KB) — bump to ~32 KB.

**Components.** `PdfSigningService`: a `string? timestampUrl` parameter threaded into `BuildDetachedCms`; a `TsaClient` helper (HTTP POST + parse). Settings: `SignTimestampUrl`. Tests: with a **mock TSA** (sign a request with a self-issued TSA cert in-test), assert the token attaches and the CMS still verifies and now carries a timestamp attribute.

**Scope/risk.** Medium. Requires network at signing time (graceful fallback to un-timestamped if the TSA is unreachable, with a clear message). Real-TSA acceptance needs manual verification in Acrobat (same caveat as the base signature).

**Effort:** **M.**

---

## 4. LTV — Long-Term Validation  · **M–L**

**Goal:** Make a signature verifiable years later without contacting CAs — by embedding the certificate chain + revocation evidence (OCSP responses / CRLs) into the PDF's DSS (Document Security Store). Builds directly on #3 (timestamp) and is the PAdES-LT/LTA tier.

**Design.** After signing (and timestamping), add a second incremental update that writes a `/DSS` dictionary into the catalog containing:
- `/Certs` — the full chain (already have the certs).
- `/OCSPs` — OCSP responses fetched from each cert's AIA OCSP URL (BouncyCastle `OcspReqGenerator` / response parsing), and/or
- `/CRLs` — CRLs from each cert's CRL distribution point.
- a `/VRI` (Validation-Related Information) entry keyed by the signature.
This reuses the **incremental-update writer** we already built for the signature — a second appended revision with the DSS objects + xref/trailer `/Prev`.

**Components.** `Services/Signing/`: a `RevocationCollector` (AIA/CDP fetch via BouncyCastle + HTTP) and a `DssWriter` that emits the DSS incremental update (extends the existing object/xref emission in `PdfSigningService`). Network at sign time. Tests: with mock OCSP/CRL, assert DSS objects present and the file re-opens.

**Scope/risk.** Medium–high. The DSS/VRI structure is intricate and, like the base signature, can only be *fully* validated against Acrobat's "LTV enabled" indicator with real certs. Best done after #3 lands and is verified.

**Effort:** **M–L.** *(Recommend gating this behind a real-Acrobat validation pass before shipping.)*

---

## 5. Visible signature appearance  · **M**

**Goal:** Optionally show the signature as a drawn field on the page (name / date / reason / a chosen drawn-signature image), instead of the current invisible signature.

**Design.** Today the signature widget is built with `/Rect [0 0 0 0]` and no appearance stream. To make it visible:
1. Let the user pick a page + drag a rectangle for placement (reuse the rubber-band selection / the existing signature-placement cursor flow).
2. Build a **Form XObject appearance stream** (`/AP /N`) for the widget: draw the chosen text lines (signer CN, `/M` date, optional reason/location) and/or a reused saved drawn-signature image (we already render those). PdfSharpCore `XGraphics` can render the XObject content; embed it in the incremental update as the widget's `/AP`.
3. Set the widget `/Rect` to the chosen page rectangle and drop the hidden `/F 132` flag.

**Components.** `PdfSigningService`: an optional `SignatureAppearance { int Page; RectPt Rect; bool ShowName, ShowDate, ShowReason; byte[]? ImagePng; }` that, when present, emits the `/AP` XObject + non-zero `/Rect` in the incremental update. UI: extend `ToolsSign_Click` with an "Appearance" step (invisible vs visible + placement). Tests: assert a visible sig produces an `/AP` stream and still verifies.

**Scope/risk.** Medium. The appearance is cosmetic (doesn't affect crypto validity) but the `/AP` XObject byte emission inside the incremental update is fiddly. Invisible remains the default; visible is opt-in.

**Effort:** **M.**

> **Note on BouncyCastle:** upstream lists "swap in a BouncyCastle signer for portability + timestamps" as a TODO because *they* use PDFsharp's signer. **Scalpel already uses BouncyCastle** (`BuildDetachedCms`), so that item is effectively done for us — and it's why #3/#4 are cheaper for us than for them.

---

## 6. Cross-platform port (Avalonia → Linux / Mac)  · **XL** *(reviewed last, by request)*

**Goal:** Run Scalpel beyond Windows/WPF. Upstream is *architecting toward this now* (keeping the signing module OS-guarded and toolkit-agnostic); it's the biggest strategic bet on their roadmap.

**Reality check — this is an order of magnitude larger than everything above.** Scalpel is deeply WPF- and Windows-bound:
- **UI:** ~30 `MainWindow.*.cs` partials + `*.xaml` are pure WPF. Avalonia is the closest port target (XAML-ish, MVVM-friendly) but every view, style, theme dictionary, and code-behind interaction needs porting/rewriting.
- **Windows-only deps:** the registry (settings/recent/install), DWM dark-titlebar P/Invoke, `user32`/`gdi32` (eyedropper, foreground), the self-installer, MSIX/Store packaging, `X509Certificate2UI`/cert store, Costura single-file bundling.
- **Native PDF stack:** `pdfium.dll` (Docnet) and the fonts ship per-RID; Linux/Mac need their own native binaries and a non-WPF render→bitmap path.
- **Target framework:** net48 → net8.0 (cross-platform) is itself a migration (PdfSharpCore is netstandard, so it travels; WPF does not).

**Recommended approach (if pursued) — phased, not a big-bang rewrite:**
1. **Extract a platform-agnostic core** (`Scalpel.Core`, net8.0): all `Services/*` that are already WPF-free (PdfSigning, Watermark, Transform, Ocr*, RecentFiles, ColorConvert, BatesNumbering, …). Most are *already* portable — this is mostly project restructuring + replacing `System.Windows.Media.Color` with a small color struct where used.
2. **Abstract the platform seams** behind interfaces: settings store (registry → JSON file on non-Windows), file dialogs, clipboard, screen capture, cert store, page rasterizer (pdfium per-RID).
3. **New Avalonia UI head** (`Scalpel.Avalonia`) consuming the core — rebuilt views, not ported XAML. Windows keeps the existing WPF head (or also moves to Avalonia long-term).
4. **Per-platform packaging** (AppImage/deb, .app/dmg) replacing the Windows self-installer.

**Scope/risk.** Very high; multi-phase, its own spec series (each phase = its own design). The signing/OCR/tools cores port cheaply; the UI and Windows integrations are the cost.

**Effort:** **XL** (a project, not a feature). **Recommendation:** treat as a separate initiative with its own brainstorm; start with phase 1 (core extraction) since it's low-risk and benefits the codebase even if the full port never ships.

---

## Suggested order

1. **Region OCR** (S) — quick parity win.
2. **Windows cert-store picker** (S–M) — high-value, crypto unchanged.
3. **RFC-3161 timestamp** (M) — the key signing upgrade; BouncyCastle already in place.
4. **Visible signature appearance** (M) — user-visible, independent of #3/#4.
5. **LTV** (M–L) — after timestamp; gate on real-Acrobat validation.
6. **Cross-platform** (XL) — separate initiative; start with core extraction only.
