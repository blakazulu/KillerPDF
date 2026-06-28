# Design Spec — OCR UX (language/quality + page-clipboard + extract-text) (Tier 2, feature 1)

**Date:** 2026-06-29
**Status:** Approved (design), proceeding to plan
**Program:** `killerpdf-feature-port-program` memory. Foundation refactor + all of Tier 1 done.
**Scope note:** OCR "region → clipboard" is intentionally DEFERRED to a follow-up cycle.

## Goal

Upgrade OCR from English-only/fast-only to: a **Language + Quality picker** (any of ~15 languages, fast or best models) that **every** OCR operation uses, plus two new operations — **OCR current page → clipboard** and **Extract all text → .txt/.md**. The existing Make-Searchable gains multilingual + HQ for free.

## Components

### 1. `Services/OcrAssets.cs` — generalize to (language, quality)
Add a `bool best = false` parameter (default preserves current behavior) to the public methods:
- `LanguageUrl(string lang, bool best = false)` — `best` ⇒ `https://github.com/tesseract-ocr/tessdata_best/raw/main/{lang}.traineddata`, else the current `tessdata_fast` URL.
- `DownloadLanguage(string lang, bool best = false)` — downloads into the quality-specific dir (atomic `.part`→final, the existing >100 KB sanity check).
- `ResolveTessdataDir(string lang, bool best = false)` and `HasLanguage(string lang, bool best = false)` — search the quality-appropriate dirs.
- New dir constant `DownloadTessdataDirBest` = `%LOCALAPPDATA%\Scalpel\ocr\tessdata-best` (fast stays in the existing `tessdata`). For `best`, search only the best dir (bundled/sibling data is fast); for fast, keep the existing bundled→sibling→download search.
- New language catalog:
  ```csharp
  public static readonly (string Code, string Name)[] Languages =
  {
      ("eng","English"), ("spa","Spanish"), ("fra","French"), ("deu","German"),
      ("por","Portuguese"), ("ita","Italian"), ("nld","Dutch"), ("rus","Russian"),
      ("chi_sim","Chinese (Simplified)"), ("chi_tra","Chinese (Traditional)"),
      ("jpn","Japanese"), ("kor","Korean"), ("ara","Arabic"), ("heb","Hebrew"), ("hin","Hindi"),
  };
  ```

### 2. `Services/OcrTextJoiner.cs` — NEW pure helper (unit-tested)
```csharp
namespace Scalpel.Services;
public static class OcrTextJoiner
{
    /// <summary>Joins recognized words into plain text: groups words into lines by vertical
    /// position (a new line when a word's top exceeds the current line's top by more than half the
    /// line's height), orders each line left-to-right, joins words with spaces and lines with "\n".
    /// Returns "" for no words.</summary>
    public static string Join(System.Collections.Generic.IReadOnlyList<OcrWord> words);
}
```
Algorithm: if empty → `""`. Order a working copy by `YPt` then `XPt`. Walk it; start a new line when `w.YPt - lineTop > 0.5 * lineHeight` (lineTop/lineHeight = the first word of the current line). Within each line keep words ordered by `XPt`; `string.Join(" ", line)`. Join lines with `"\n"`. Trim trailing whitespace per line.

### 3. `EnsureOcrReady()` helper (`MainWindow.Tools.cs`)
Returns a small tuple/struct `(string exe, string tessdata, string lang)?` or null:
- `lang = App.GetSetting("OcrLanguage") ?? "eng"`, `bool best = App.GetSetting("OcrHighQuality") == "1"`.
- `exe = OcrAssets.FindTesseractExe();` if null → the existing "install Tesseract" dialog → return null.
- if `!OcrAssets.HasLanguage(lang, best)` → the existing download prompt (generalized to name the language + "high-quality" when best) → `await DownloadLanguage(lang, best)`; on failure → error dialog → null.
- `tessdata = OcrAssets.ResolveTessdataDir(lang, best);` return `(exe, tessdata, lang)`.
(Since downloading is async, `EnsureOcrReady` is `async Task<...>`.)

**Refactor `ToolsOcr_Click`** to call `EnsureOcrReady()` (removing the hardcoded `"eng"` and inline exe/download logic), then `new TesseractCliOcrEngine(exe, tessdata, lang)` as today.

### 4. Three new handlers + Tools menu items (`MainWindow.xaml` ContextMenu + `MainWindow.Tools.cs`)
- **`ToolsOcrLanguage_Click`** → `ShowToolForm("OCR Language", [Language combo (Languages display names), Quality combo ("Fast","Best (slower, more accurate)")], "Save")`. Pre-select the current settings. On OK: map the chosen display name back to its code; `App.SetSetting("OcrLanguage", code)`, `App.SetSetting("OcrHighQuality", quality=="Best"?"1":"0")`; status confirmation.
- **`ToolsOcrPageToClipboard_Click`** → `RequireOpenDoc`; `var r = await EnsureOcrReady(); if (r is null) return;` save `_doc` to a temp (`var src = App.MakeTempFile("ocrclip"); _doc.Save(src);`); on a background thread: `using var rast = new DocnetPageRasterizer(src, 2000); var raster = rast.RenderPage(curPage); var (wPt,hPt)=rast.PageSizePt(curPage); var ocr = new TesseractCliOcrEngine(r.exe, r.tessdata, r.lang).Recognize(raster.ImageBytes, wPt, hPt); var text = OcrTextJoiner.Join(ocr.Words);` then `Clipboard.SetText(text)` on the UI thread; status "Copied N words". `curPage` = the app's current page index (confirm the field name in the codebase).
- **`ToolsOcrExtractText_Click`** → `RequireOpenDoc`; `EnsureOcrReady`; `SaveFileDialog` (filter "Text|*.txt|Markdown|*.md"); save `_doc` to a temp; background loop over all pages: recognize + `OcrTextJoiner.Join`; build the document — for `.md`, prefix each page with `## Page {n}\n\n`; for `.txt`, separate pages with a blank line; write the file; status with page count. Per-page status updates ("OCR page i of n").

All three reuse the existing async + try/catch + `Logger.Error("Tools", ...)` pattern from `ToolsOcr_Click`.

### 5. Settings
`OcrLanguage` (code, default "eng") and `OcrHighQuality` ("1"/"0", default "0") in `HKCU\Software\Scalpel\Settings` via `App.GetSetting`/`SetSetting`.

### 6. Localization (all 9 `Strings/*.xaml`) + changelog
New `Str_*` keys (English shown; translate the rest; RTL he/ar):
- `Str_Tool_OcrLanguage` = "OCR Language…"
- `Str_Tool_OcrClipboard` = "OCR Page to Clipboard"
- `Str_Tool_OcrExtract` = "Extract All Text…"
- `Str_Ocr_LangLabel` = "Language", `Str_Ocr_QualityLabel` = "Quality"
- `Str_Ocr_QualityFast` = "Fast", `Str_Ocr_QualityBest` = "Best (slower, more accurate)"
- `Str_Ocr_LangSaved` = "OCR language saved."
- `Str_Ocr_Copied` = "Copied OCR text to clipboard."
- `Str_Ocr_ExtractDone` = "Extracted text to file."
- `Str_Ocr_DownloadPrompt` = "Download the {0} OCR language data once (~10–30 MB) into %LOCALAPPDATA%\\Scalpel\\ocr? Everything stays on your machine." (the handler may format the language name in; if string.Format is awkward with localized text, use a generic message without the name.)
Changelog: one bullet — OCR now supports many languages and a high-quality mode, can copy a page's text to the clipboard, and can extract all text to a .txt/.md file.

## Testing
- **Unit (xUnit):** new `OcrTextJoinerTests` covering: empty → `""`; single word → that word; two words same `YPt` different `XPt` → joined left-to-right with a space; two words clearly different `YPt` → two lines in top-to-bottom order; out-of-order input still sorted correctly. Link `Services/OcrTextJoiner.cs` AND `Services/OcrService.cs` (for `OcrWord`) into `Scalpel.Tests.csproj` if not already linked.
- **Build gate:** `~/.dotnet/dotnet.exe build` clean; full suite green (187 + new tests).
- **Locale check:** `grep -L "Str_Tool_OcrLanguage" Strings/*.xaml` → nothing.
- **Manual smoke (owed — needs Tesseract installed):** OCR Language… → pick French + Best → Save; Extract All Text on a French scan produces a correct .txt; OCR Page to Clipboard copies the page text; Make-Searchable now uses the chosen language. Missing language data triggers the download prompt once.

## Out of scope (YAGNI)
- Region-OCR (separate later cycle), multi-language strings like `eng+fra`, auto language detection, per-language quality memory, a progress bar (status text only).

## Definition of done
A Language+Quality picker persists a global OCR preference that Make-Searchable, OCR-Page-to-Clipboard, and Extract-All-Text all honor; missing language data downloads on demand (fast or best); the two new operations work; `OcrTextJoinerTests` green; build clean; all 9 locales + changelog updated.
