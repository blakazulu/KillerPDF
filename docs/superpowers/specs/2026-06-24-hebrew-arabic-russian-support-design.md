# Hebrew / Arabic / Russian full language support — design

**Date:** 2026-06-24
**Status:** Approved (design)

## Goal

Add full Hebrew, Arabic, and Russian support to Scalpel:

1. **Text rendering** — typing Hebrew/Arabic/Russian into PDF annotations and saving must
   produce correct output: RTL text in correct (logical) reading order — `שלום` must read
   `שלום`, never the reversed `מולש` — Arabic letters cursively connected, and Cyrillic
   glyphs present (not missing boxes).
2. **UI languages** — Hebrew, Arabic, and Russian selectable in Settings, with the whole UI
   mirrored right-to-left when a Hebrew/Arabic locale is active.
3. **Fonts** — bundle and register the fonts needed to cover these scripts in the PDF burn path.
4. **Testing** — unit tests proving non-reversal + Arabic shaping, plus an end-to-end test
   PDF containing all three scripts that is re-extracted and asserted to read correctly.

## Background: what already exists (the Hebrew foundation)

A prior cycle shipped Hebrew text editing. We extend it rather than rebuild:

- `Services/BidiReorder.cs` — pure logical→visual reorderer. `IsRtl()` currently covers only
  Hebrew (U+0590–05FF, U+FB1D–FB4F).
- `Resources/Fonts/NotoSansHebrew-Regular.ttf` — bundled, registered in `App.RegisterPdfFonts()`
  via `PdfFontResolver.Instance.RegisterBundledFont("Noto Sans Hebrew", …)`.
- `MainWindow.DrawTextRun()` (MainWindow.xaml.cs:8073) — at PDF save it reorders RTL to visual
  order, picks a Hebrew-capable font (`FontHasHebrew`), right-aligns, and draws via PdfSharpCore
  `XGraphics.DrawString`. The LTR path is unchanged.
- WPF overlay `TextBox` (PlaceTextBox, line 6682) sets `FontFamily = "Segoe UI, Noto Sans Hebrew"`
  and flips `FlowDirection` to RightToLeft when `BidiReorder.ContainsRtl` is true.
- Localization: `Services/LocaleManager.cs` with 6 locales (EnUS, Es, ZhTW, ZhCN, Bn, TrTR),
  **164 `Str_` keys per locale file** in `Strings/*.xaml`, wired through an enum + pack URI +
  a Settings `RadioButton` + a `Lang…Radio_Checked` handler + a sync line in `SettingsBtn_Click`.
- Tests (xUnit, source-linked): `BidiReorderTests`, `FontEmbeddingTests` (incl. Hebrew embed),
  `SearchServiceTests`, `TrueTypeCmapTests`.

## The three scripts are three distinct problems

### Russian / Cyrillic — LTR, font-coverage only

Cyrillic (U+0400–04FF) is left-to-right; no bidi, no shaping. The only real gap is the **save
path**: new `TextAnnotation`s draw with the hard-coded candidate font `"Geist"`, which has no
Cyrillic glyphs, so Russian would burn in as `.notdef` boxes. WPF live editing already works
(Segoe UI covers Cyrillic).

**Fix:** bundle a Cyrillic-capable face (`NotoSans-Regular.ttf`, Latin+Cyrillic) and add a
coverage fallback in the LTR branch of `DrawTextRun` so Cyrillic text falls back to it when the
candidate font lacks coverage. Generalized below.

### Hebrew — already working; add as a UI language

Functionally complete. Work is limited to adding the `he` locale + Settings entry + RTL mirroring.

### Arabic — the real lift: bidi + cursive shaping

Two sub-problems:

1. **Bidi reversal** — Arabic is RTL. Extend `BidiReorder.IsRtl()` to recognize Arabic base
   (U+0600–06FF), Arabic Supplement (U+0750–077F), and the presentation-forms blocks
   (U+FB50–FDFF "A", U+FE70–FEFF "B"). This alone stops `سلام` from reversing.

2. **Cursive shaping** — PdfSharpCore's `DrawString` maps Unicode→glyph via `cmap` with **no
   GSUB**, so raw Arabic draws as disconnected *isolated* letters in wrong forms. We add a pure
   `Services/ArabicShaper.cs` implementing the Arabic joining algorithm:
   - Classify each character's joining type (dual-join, right-join, non-join, transparent for
     combining marks).
   - Choose each letter's contextual form (isolated / initial / medial / final) from its
     joining neighbors and map to the corresponding **Arabic Presentation Forms-B** codepoint
     (U+FE70–FEFF), which `NotoSansArabic` includes in its cmap → connected glyphs.
   - Apply the mandatory **Lam-Alef** ligatures (U+FEF5–FEFC): collapse `lam`+`alef` into one
     glyph.
   - Combining marks (harakat) are "transparent": they don't break joining and keep their
     position. Pure function, never throws (defensive `try/catch` → return input).

   Pipeline in the save path becomes: **shape (in logical order) → `BidiReorder.ToVisual` →
   draw**. Shaping in logical order before reversal is correct because once a letter's
   presentation form is chosen from its logical neighbors, the resulting glyph is
   position-independent and can be safely reversed as a unit (incl. the single lam-alef glyph).

### Generalized font selection in `DrawTextRun`

Replace the Hebrew-only `FontHasHebrew` check with a small script→face chooser:

- Latin/default → `"Geist"` (candidate)
- Hebrew present → `"Noto Sans Hebrew"`
- Arabic present → `"Noto Sans Arabic"`
- Cyrillic present (and candidate lacks it) → `"Noto Sans"`

Selection is by the dominant script of the run (annotations are effectively single-script;
mixed-script is an accepted edge case). Coverage is verified with the existing
`TrueTypeCmap.CoversCodepoint` against a representative codepoint per script. If a bundled face
isn't registered (resource missing), fall back to the candidate — never throw mid-save.

## Components

| Component | Type | Responsibility |
|-----------|------|----------------|
| `Services/ArabicShaper.cs` | new, pure static | logical Arabic → presentation-form string (joining + lam-alef). Testable, never throws. |
| `Services/BidiReorder.cs` | modify | extend `IsRtl()` to Arabic + presentation-forms ranges. |
| `MainWindow.DrawTextRun` | modify | insert Arabic shaping before reorder; generalize font choice across Hebrew/Arabic/Cyrillic. |
| `App.RegisterPdfFonts` | modify | register `Noto Sans Arabic` + `Noto Sans` (Cyrillic) bundled faces. |
| `Resources/Fonts/NotoSansArabic-Regular.ttf` | new asset | Arabic glyphs incl. presentation forms. SIL OFL. |
| `Resources/Fonts/NotoSans-Regular.ttf` | new asset | Latin+Cyrillic for Russian burn-in. SIL OFL. |
| WPF overlay TextBoxes (3 sites) | modify | extend `FontFamily` fallback list to include Arabic; `FlowDirection` already keys on `ContainsRtl` (now Arabic-aware). |
| `Services/LocaleManager.cs` | modify | add `He`, `Ar`, `Ru` to `Locale` enum + pack URIs; apply window-level `FlowDirection` for RTL locales. |
| `Strings/he.xaml`, `ar.xaml`, `ru.xaml` | new | all 164 `Str_` keys translated. |
| `Strings/*.xaml` (all 9) | modify | add `Str_Lang_Hebrew`, `Str_Lang_Arabic`, `Str_Lang_Russian`. |
| `MainWindow.xaml` + `.xaml.cs` | modify | 3 Settings radios + handlers + `SettingsBtn_Click` sync. |

## RTL UI mirroring

`LocaleManager.Apply` sets `Application.Current.MainWindow.FlowDirection` to `RightToLeft` for
`He`/`Ar` and `LeftToRight` otherwise. WPF mirrors child layout automatically. Elements that must
stay LTR regardless (logo, certain icon glyphs, the zoom %/page-number numeric fields) get an
explicit `FlowDirection="LeftToRight"` override in XAML. The mirror is applied on locale switch
and re-applied at startup after the window is created. Risk: the custom Studio toolbar may need a
few local overrides — handled during implementation/verification, not redesigned.

## Data flow (PDF save, RTL text)

```
annotation.Content (logical, e.g. "سلام عليكم")
  → contains Arabic?  → ArabicShaper.Shape()   // logical-order presentation forms + lam-alef
  → BidiReorder.ToVisual()                      // reverse RTL runs to visual order
  → choose face covering script (Noto Sans Arabic)
  → measure width, right-align
  → XGraphics.DrawString(visual, font, …)       // PdfFontResolver embeds the face
```

For Hebrew the shaping step is a no-op (Hebrew is non-cursive). For Russian/Latin both shaping
and reorder are no-ops; only the coverage fallback applies.

## Error handling

Follows the codebase convention: all new parsing/shaping is wrapped in defensive `try/catch`
that fall back to the input/candidate so a malformed string or missing font never breaks a save.
`ArabicShaper` and `BidiReorder` are pure and never throw. Missing font resources are skipped
(as the existing Hebrew registration already does).

## Testing

1. **`ArabicShaperTests`** (new) — joining forms: a dual-join letter between two joiners → medial
   form; word-initial → initial form; isolated letter → isolated; `lam`+`alef` → the single
   ligature codepoint; harakat don't break joining; empty/Latin input unchanged.
2. **`BidiReorderTests`** (extend) — `ContainsRtl` detects Arabic; an Arabic word's visual order
   is the reverse of its logical order; mixed Arabic+digits keeps digits LTR.
3. **`FontEmbeddingTests`** (extend) — `NotoSansArabic` covers U+0627 and embeds; `NotoSans`
   covers Cyrillic U+0410 and embeds.
4. **End-to-end multilingual PDF test** (new) — build a PDF via the same draw helpers containing
   Hebrew `שלום`, Arabic `سلام`, Russian `Привет`; re-extract text with PdfPig and assert each
   string appears in correct **logical** order (explicitly assert `שלום` is present and the
   reversed `מולש` is **absent**), and that the three fonts are embedded. This is the canonical
   "not-backwards" guarantee. The generated PDF is also saved under `docs/` / test output as the
   reusable multilingual sample the goal asks for.

**Known limitation (carried over from Hebrew, accepted for v1):** full-text *search* over text
that Scalpel itself burned in stores visual order, so searching our own freshly-edited RTL text
may not match; real-world RTL PDFs (logical order) search fine. Documented, not fixed here.

## Out of scope

- A complete Unicode Bidi Algorithm (nested embeddings, explicit directional marks) — the
  run-based approximation covers single-paragraph annotation text.
- Arabic search-order normalization.
- Vertical scripts, Indic shaping, complex ligature sets beyond lam-alef.
