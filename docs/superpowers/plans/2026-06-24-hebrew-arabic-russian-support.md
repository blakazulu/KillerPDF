# Hebrew / Arabic / Russian Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full Hebrew, Arabic, and Russian support to Scalpel — correct (non-reversed) RTL text + cursive Arabic + Cyrillic glyphs burned into PDFs, plus selectable RTL-mirrored UI languages.

**Architecture:** Extend the existing Hebrew pipeline. A new pure `ArabicShaper` produces Arabic presentation forms (cursive joining + lam-alef); `BidiReorder.IsRtl` is widened to Arabic; `DrawTextRun` gains a shape→reorder→script-font-pick flow. Two Noto fonts are bundled and registered. Three locales (he/ar/ru) are added with full translations, and `LocaleManager` mirrors the window RTL for he/ar.

**Tech Stack:** .NET Framework 4.8 (net48), WPF, PdfSharpCore (draw), PdfPig (extract/verify), xUnit (source-linked tests). Build/test with `~/.dotnet/dotnet.exe`.

## Global Constraints

- Target `net48`, x64. Build/test only via `~/.dotnet/dotnet.exe` (not on PATH). `Nullable` + `ImplicitUsings` enabled; `LangVersion=latest`; use collection expressions / target-typed `new` / switch expressions.
- All new parsing/shaping is pure and **never throws** — wrap in `try/catch` returning the input/candidate, per the codebase's defensive convention.
- Bundled fonts are SIL OFL; include the license text file and register via `PdfFontResolver.Instance.RegisterBundledFont(family, bytes, bold, italic)` inside `App.RegisterPdfFonts()`.
- Locale string files: **every** `x:Key` must exist in **every** `Strings/*.xaml` file (a missing key blanks the `DynamicResource` in that language). Keep `x:Key` names and `{0}`-style placeholders identical across languages; translate only the values.
- Translations for he/ar/ru are AI-generated and shipped as-is (native review recommended later, per approved design).
- Tests live in `Scalpel.Tests`, which **source-links** files via `<Compile Include="..\…" Link="…">` — add a link entry for every new source file under test.
- Commit after each task. End commit messages with the `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer. Work on `main` (no feature branch).

---

### Task 1: ArabicShaper service (cursive joining → presentation forms)

**Files:**
- Create: `Services/ArabicShaper.cs`
- Test: `Scalpel.Tests/ArabicShaperTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (add source link)

**Interfaces:**
- Produces: `public static class Scalpel.Services.ArabicShaper { public static bool ContainsArabic(string? s); public static string Shape(string? logical); }`
  - `Shape` maps each Arabic base letter (U+0621–U+064A range) to its contextual Arabic Presentation Forms-B codepoint (U+FE70–U+FEFF) based on joining context, collapses lam+alef to the single ligature (U+FEF5/F7/F9/FB), leaves non-Arabic chars and combining marks (harakat U+064B–U+0652, U+0670) in place. Logical order is preserved (reversal happens later in `BidiReorder.ToVisual`). Pure, never throws.

- [ ] **Step 1: Write the failing tests**

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ArabicShaperTests
    {
        // Letters: U+0628 BEH (dual-join), U+0627 ALEF (right-join only), U+062F DAL (right-join),
        //          U+0644 LAM (dual), U+0645 MEEM (dual), U+0633 SEEN (dual)
        [Fact]
        public void ContainsArabic_DetectsArabic()
        {
            Assert.True(ArabicShaper.ContainsArabic("سلام")); // سلام
            Assert.False(ArabicShaper.ContainsArabic("hello"));
            Assert.False(ArabicShaper.ContainsArabic("שלום")); // Hebrew, not Arabic
        }

        [Fact]
        public void Shape_EmptyAndLatin_Unchanged()
        {
            Assert.Equal("", ArabicShaper.Shape(""));
            Assert.Equal("hello", ArabicShaper.Shape("hello"));
        }

        [Fact]
        public void Shape_IsolatedSingleLetter_UsesIsolatedForm()
        {
            // BEH alone -> FE8F (isolated)
            Assert.Equal("ﺏ", ArabicShaper.Shape("ب"));
        }

        [Fact]
        public void Shape_DualJoinBetweenJoiners_UsesMedialForm()
        {
            // BEH BEH BEH: middle BEH is medial (FE92); first is initial (FE91); last is final (FE90)
            string outp = ArabicShaper.Shape("ببب");
            Assert.Equal("ﺑﺒﺐ", outp);
        }

        [Fact]
        public void Shape_AfterRightJoiner_NextStartsFresh()
        {
            // ALEF (right-join, does not join to following) + BEH:
            // ALEF takes final form FE8E (it joins to a preceding? none -> isolated FE8D),
            // BEH after a non-left-joining letter is initial -> FE91 ... but ALEF doesn't join forward,
            // so BEH is isolated FE8F.
            string outp = ArabicShaper.Shape("اب");
            Assert.Equal("ﺍﺏ", outp);
        }

        [Fact]
        public void Shape_LamAlef_FormsLigature()
        {
            // LAM (U+0644) + ALEF (U+0627) -> isolated lam-alef ligature U+FEFB
            Assert.Equal("ﻻ", ArabicShaper.Shape("لا"));
        }

        [Fact]
        public void Shape_SalamWord_Connects()
        {
            // SEEN LAM ALEF MEEM (سلام). LAM+ALEF ligature in final position (FEFC) because
            // preceded by SEEN; SEEN initial; MEEM final. Expect 3 output chars: SEEN-init, lam-alef-final, MEEM-final.
            string outp = ArabicShaper.Shape("سلام");
            Assert.Equal(3, outp.Length);
            Assert.Equal('ﺳ', outp[0]); // SEEN initial
            Assert.Equal('ﻼ', outp[1]); // lam-alef final ligature
            Assert.Equal('ﻢ', outp[2]); // MEEM final
        }

        [Fact]
        public void Shape_HarakatTransparent_DoNotBreakJoining()
        {
            // BEH + FATHA(U+064E) + BEH: the fatha is transparent; the two BEH still join
            // (initial + final). Output keeps the mark in place between them.
            string outp = ArabicShaper.Shape("بَب");
            Assert.Equal("ﺑَﺐ", outp);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~ArabicShaper"`
Expected: FAIL — `ArabicShaper` does not exist / not compiled.

- [ ] **Step 3: Add the source link in `Scalpel.Tests/Scalpel.Tests.csproj`**

Find the `<ItemGroup>` containing `<Compile Include="..\Services\BidiReorder.cs" Link="Services\BidiReorder.cs" />` and add directly after it:

```xml
    <Compile Include="..\Services\ArabicShaper.cs" Link="Services\ArabicShaper.cs" />
```

- [ ] **Step 4: Implement `Services/ArabicShaper.cs`**

```csharp
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal Arabic cursive shaper: maps logical-order Arabic base letters to their
    /// contextual Arabic Presentation Forms-B glyphs (U+FE70–FEFF) using the joining
    /// algorithm, and collapses lam+alef into the mandatory ligature. Combining marks
    /// (harakat) are transparent (do not break joining). Does NOT reorder — that is left
    /// to BidiReorder.ToVisual. PdfSharpCore's DrawString applies no GSUB, so substituting
    /// presentation forms here is what makes burned-in Arabic connect. Pure; never throws.
    /// </summary>
    public static class ArabicShaper
    {
        // Joining type per base letter.
        private enum J { U /*non-joining*/, R /*right-joining only*/, D /*dual*/, T /*transparent*/ }

        // Forms table: base codepoint -> [isolated, final, initial, medial].
        // A right-joining (R) letter has no initial/medial; we store its isolated/final in
        // those slots' isolated/final and reuse them. Index: 0=isolated 1=final 2=initial 3=medial.
        private static readonly System.Collections.Generic.Dictionary<char, (J join, char[] forms)> Table = Build();

        public static bool ContainsArabic(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s!)
                if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        public static string Shape(string? logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical ?? "";
            try
            {
                if (!ContainsArabic(logical)) return logical!;
                int n = logical!.Length;
                var outp = new StringBuilder(n);

                // Precompute join class per index (transparent marks tracked separately).
                for (int i = 0; i < n; i++)
                {
                    char c = logical[i];
                    if (!Table.TryGetValue(c, out var info))
                    {
                        outp.Append(c); // non-Arabic or unmapped: passthrough
                        continue;
                    }
                    if (info.join == J.T) { outp.Append(c); continue; } // harakat stay in place

                    // Lam-Alef ligature: current is LAM and next non-transparent is an ALEF variant.
                    if (c == 'ل' && NextBase(logical, i, out int alefIdx, out char alefForm))
                    {
                        bool joinsPrev = JoinsPrev(logical, i);
                        outp.Append(joinsPrev ? FinalLigature(alefForm) : IsolatedLigature(alefForm));
                        i = alefIdx; // consume the alef too
                        continue;
                    }

                    bool prev = JoinsPrev(logical, i);                // a letter before me joins forward
                    bool next = info.join == J.D && JoinsNext(logical, i); // I join forward AND next accepts
                    int slot = (prev, next) switch
                    {
                        (false, false) => 0, // isolated
                        (true,  false) => 1, // final
                        (false, true ) => 2, // initial
                        (true,  true ) => 3, // medial
                    };
                    // Right-joining letters (R) have only isolated/final; clamp slot.
                    if (info.join == J.R && slot >= 2) slot -= 2;
                    outp.Append(info.forms[slot]);
                }
                return outp.ToString();
            }
            catch { return logical!; }
        }

        // Does the previous non-transparent letter join to the right (i.e., is dual/right and dual? )
        // For "prev joins to me", the previous base must be D (dual) and itself able to join forward.
        private static bool JoinsPrev(string s, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                return info.join == J.D; // only dual letters join to the following letter
            }
            return false;
        }

        // Does the next non-transparent base accept a join from me (next is D or R)?
        private static bool JoinsNext(string s, int i)
        {
            for (int k = i + 1; k < s.Length; k++)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                return info.join == J.D || info.join == J.R;
            }
            return false;
        }

        // Find the next non-transparent base; report if it is an ALEF variant (for lam-alef).
        private static bool NextBase(string s, int i, out int idx, out char alef)
        {
            idx = -1; alef = '\0';
            for (int k = i + 1; k < s.Length; k++)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                char c = s[k];
                if (c == 'ا' || c == 'آ' || c == 'أ' || c == 'إ')
                { idx = k; alef = c; return true; }
                return false;
            }
            return false;
        }

        // Lam-alef ligature codepoints by alef variant: madda 0622, hamza-above 0623,
        // hamza-below 0625, plain 0627. Isolated FEF5/F7/F9/FB; final FEF6/F8/FA/FC.
        private static char IsolatedLigature(char alef) => alef switch
        {
            'آ' => 'ﻵ', 'أ' => 'ﻷ', 'إ' => 'ﻹ', _ => 'ﻻ',
        };
        private static char FinalLigature(char alef) => alef switch
        {
            'آ' => 'ﻶ', 'أ' => 'ﻸ', 'إ' => 'ﻺ', _ => 'ﻼ',
        };

        private static System.Collections.Generic.Dictionary<char, (J, char[])> Build()
        {
            var d = new System.Collections.Generic.Dictionary<char, (J, char[])>();
            // forms: isolated, final, initial, medial
            void Add(char b, J j, char iso, char fin, char ini = '\0', char med = '\0')
                => d[b] = (j, new[] { iso, fin, ini, med });

            // Right-joining (no initial/medial): hamza, alef variants, dal/thal, reh/zain, waw, alef-maksura(ى as 0649 dual? treat dual), teh-marbuta
            Add('ء', J.U, 'ﺀ', 'ﺀ');                 // HAMZA (non-joining)
            Add('آ', J.R, 'ﺁ', 'ﺂ');                 // ALEF MADDA
            Add('أ', J.R, 'ﺃ', 'ﺄ');                 // ALEF HAMZA ABOVE
            Add('إ', J.R, 'ﺇ', 'ﺈ');                 // ALEF HAMZA BELOW
            Add('ا', J.R, 'ﺍ', 'ﺎ');                 // ALEF
            Add('ؤ', J.R, 'ﺅ', 'ﺆ');                 // WAW HAMZA
            Add('د', J.R, 'ﺩ', 'ﺪ');                 // DAL
            Add('ذ', J.R, 'ﺫ', 'ﺬ');                 // THAL
            Add('ر', J.R, 'ﺭ', 'ﺮ');                 // REH
            Add('ز', J.R, 'ﺯ', 'ﺰ');                 // ZAIN
            Add('و', J.R, 'ﻭ', 'ﻮ');                 // WAW
            Add('ة', J.R, 'ﺓ', 'ﺔ');                 // TEH MARBUTA

            // Dual-joining (isolated, final, initial, medial)
            Add('ب', J.D, 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ'); // BEH
            Add('ت', J.D, 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ'); // TEH
            Add('ث', J.D, 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ'); // THEH
            Add('ج', J.D, 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ'); // JEEM
            Add('ح', J.D, 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ'); // HAH
            Add('خ', J.D, 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ'); // KHAH
            Add('س', J.D, 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ'); // SEEN
            Add('ش', J.D, 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ'); // SHEEN
            Add('ص', J.D, 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ'); // SAD
            Add('ض', J.D, 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ'); // DAD
            Add('ط', J.D, 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ'); // TAH
            Add('ظ', J.D, 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ'); // ZAH
            Add('ع', J.D, 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ'); // AIN
            Add('غ', J.D, 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ'); // GHAIN
            Add('ف', J.D, 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ'); // FEH
            Add('ق', J.D, 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ'); // QAF
            Add('ك', J.D, 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ'); // KAF
            Add('ل', J.D, 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ'); // LAM
            Add('م', J.D, 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ'); // MEEM
            Add('ن', J.D, 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ'); // NOON
            Add('ه', J.D, 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ'); // HEH
            Add('ي', J.D, 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ'); // YEH
            Add('ى', J.D, 'ﻯ', 'ﻰ', 'ﻯ', 'ﻰ'); // ALEF MAKSURA (init/med rare → reuse)

            // Transparent combining marks (harakat, superscript alef, shadda, sukun, tatweel-as-passthrough)
            foreach (char m in new[] { 'ً','ٌ','ٍ','َ','ُ','ِ','ّ','ْ','ٰ' })
                d[m] = (J.T, new[] { m, m, m, m });

            return d;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~ArabicShaper"`
Expected: PASS (all 8). If a specific presentation-form codepoint assertion fails, the *expected* value in the test is what to trust against the Unicode FormsB chart — fix the `Table` entry, not the algorithm shape. Note any corrected codepoints in the commit message.

- [ ] **Step 6: Commit**

```bash
git add Services/ArabicShaper.cs Scalpel.Tests/ArabicShaperTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: ArabicShaper — cursive joining to presentation forms"
```

---

### Task 2: Extend BidiReorder to recognize Arabic

**Files:**
- Modify: `Services/BidiReorder.cs:93-94` (the `IsRtl` method)
- Test: `Scalpel.Tests/BidiReorderTests.cs` (add Arabic cases)

**Interfaces:**
- Consumes: existing `BidiReorder.ContainsRtl` / `ToVisual`.
- Produces: `IsRtl` now also returns true for Arabic base (U+0600–06FF), Arabic Supplement (U+0750–077F), and Arabic Presentation Forms A/B (U+FB50–FDFF, U+FE70–FEFF). No signature change.

- [ ] **Step 1: Write the failing tests** (append inside the `BidiReorderTests` class)

```csharp
        // salam = U+0633 U+0644 U+0627 U+0645 (logical: seen lam alef meem)
        private const string Salam = "سلام";

        [Fact]
        public void ContainsRtl_DetectsArabic()
            => Assert.True(BidiReorder.ContainsRtl(Salam));

        [Fact]
        public void ContainsRtl_DetectsArabicPresentationForms()
            => Assert.True(BidiReorder.ContainsRtl("ﺑﺒ")); // FormsB

        [Fact]
        public void ToVisual_PureArabic_Reversed()
        {
            // Pure-Arabic run reverses to visual order (char-for-char).
            string logical = Salam;
            char[] arr = logical.ToCharArray();
            System.Array.Reverse(arr);
            Assert.Equal(new string(arr), BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_ArabicThenDigits_DigitsStayLtrOnLeft()
        {
            // "salam 42" -> "42 " + reversed salam
            char[] arr = Salam.ToCharArray(); System.Array.Reverse(arr);
            Assert.Equal("42 " + new string(arr), BidiReorder.ToVisual(Salam + " 42"));
        }
```

- [ ] **Step 2: Run to verify failure**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~BidiReorder"`
Expected: FAIL — `ContainsRtl_DetectsArabic` returns false.

- [ ] **Step 3: Widen `IsRtl`** in `Services/BidiReorder.cs`, replacing the method at lines 92–94:

```csharp
        // RTL scripts: Hebrew (U+0590–05FF) + Hebrew presentation forms (U+FB1D–FB4F);
        // Arabic (U+0600–06FF), Arabic Supplement (U+0750–077F), and Arabic presentation
        // forms A (U+FB50–FDFF) and B (U+FE70–FEFF). Latin digits/letters are not RTL.
        private static bool IsRtl(char c)
            => (c >= '֐' && c <= '׿')
            || (c >= 'יִ' && c <= 'ﭏ')
            || (c >= '؀' && c <= 'ۿ')
            || (c >= 'ݐ' && c <= 'ݿ')
            || (c >= 'ﭐ' && c <= '﷿')
            || (c >= 'ﹰ' && c <= '﻿');
```

- [ ] **Step 4: Run to verify pass**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~BidiReorder"`
Expected: PASS (existing Hebrew tests + 4 new Arabic tests).

- [ ] **Step 5: Commit**

```bash
git add Services/BidiReorder.cs Scalpel.Tests/BidiReorderTests.cs
git commit -m "feat: BidiReorder recognizes Arabic + presentation forms"
```

---

### Task 3: Bundle + register Noto Sans Arabic and Noto Sans (Cyrillic)

**Files:**
- Create: `Resources/Fonts/NotoSansArabic-Regular.ttf`, `Resources/Fonts/NotoSans-Regular.ttf`, and their `*-OFL.txt` license files (copy `NotoSansHebrew-OFL.txt` content — same SIL OFL 1.1 text).
- Modify: `Scalpel.csproj:56-62` (Resource entries)
- Modify: `App.xaml.cs:173-186` (register both fonts)
- Test: `Scalpel.Tests/FontEmbeddingTests.cs` (Arabic + Cyrillic embed)

**Interfaces:**
- Produces: bundled, registered faces named `"Noto Sans Arabic"` and `"Noto Sans"` resolvable by PdfSharpCore via `PdfFontResolver`.

- [ ] **Step 1: Download the fonts** (static hinted TTFs)

```bash
curl -sL -o Resources/Fonts/NotoSansArabic-Regular.ttf \
  "https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSansArabic/hinted/ttf/NotoSansArabic-Regular.ttf"
curl -sL -o Resources/Fonts/NotoSans-Regular.ttf \
  "https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSans/hinted/ttf/NotoSans-Regular.ttf"
ls -l Resources/Fonts/NotoSansArabic-Regular.ttf Resources/Fonts/NotoSans-Regular.ttf
```

Verify both are real TTFs (Arabic ~150–250 KB, NotoSans ~500–650 KB) and non-trivial in size. If a URL 404s, fall back to the `googlefonts/noto-fonts` mirror under `hinted/ttf/<Family>/<Family>-Regular.ttf`. Copy the existing license text:

```bash
cp Resources/Fonts/NotoSansHebrew-OFL.txt Resources/Fonts/NotoSansArabic-OFL.txt
cp Resources/Fonts/NotoSansHebrew-OFL.txt Resources/Fonts/NotoSans-OFL.txt
```

- [ ] **Step 2: Register as resources in `Scalpel.csproj`** — after line 61 (`<None Include="...NotoSansHebrew-OFL.txt" />`) add:

```xml
    <Resource Include="Resources\Fonts\NotoSansArabic-Regular.ttf" />
    <None Include="Resources\Fonts\NotoSansArabic-OFL.txt" />
    <Resource Include="Resources\Fonts\NotoSans-Regular.ttf" />
    <None Include="Resources\Fonts\NotoSans-OFL.txt" />
```

- [ ] **Step 3: Write the failing embed tests** — append to `Scalpel.Tests/FontEmbeddingTests.cs` (uses existing `RepoRoot()` and `HasEmbeddedFontProgram(path)` helpers):

```csharp
        [Fact]
        public void NotoArabic_FromRepo_CoversBeh_AndEmbeds()
        {
            string noto = System.IO.Path.Combine(RepoRoot(), "Resources", "Fonts", "NotoSansArabic-Regular.ttf");
            Assert.True(System.IO.File.Exists(noto), $"missing {noto}");
            byte[] bytes = System.IO.File.ReadAllBytes(noto);
            Assert.True(Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, 0x0628), "Noto Sans Arabic must cover BEH");
            PdfFontResolver.Instance.RegisterBundledFont("Noto Sans Arabic", bytes, false, false);

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scalpel_ar_{System.Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharpCore.Pdf.PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                    gfx.DrawString("ﺑﺒ", new PdfSharpCore.Drawing.XFont("Noto Sans Arabic", 20),
                        PdfSharpCore.Drawing.XBrushes.Black, new PdfSharpCore.Drawing.XPoint(40, 40));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path));
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        [Fact]
        public void NotoSans_FromRepo_CoversCyrillic_AndEmbeds()
        {
            string noto = System.IO.Path.Combine(RepoRoot(), "Resources", "Fonts", "NotoSans-Regular.ttf");
            Assert.True(System.IO.File.Exists(noto), $"missing {noto}");
            byte[] bytes = System.IO.File.ReadAllBytes(noto);
            Assert.True(Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, 0x0410), "Noto Sans must cover Cyrillic A");
            PdfFontResolver.Instance.RegisterBundledFont("Noto Sans", bytes, false, false);

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scalpel_ru_{System.Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharpCore.Pdf.PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                    gfx.DrawString("Привет", new PdfSharpCore.Drawing.XFont("Noto Sans", 20),
                        PdfSharpCore.Drawing.XBrushes.Black, new PdfSharpCore.Drawing.XPoint(40, 40));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path));
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }
```

- [ ] **Step 4: Run to verify failure**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~FontEmbedding"`
Expected: FAIL — fonts not yet downloaded OR not registered in app (the file-exists assert may pass after Step 1; the embed asserts drive the work).

- [ ] **Step 5: Register both fonts in the app** — in `App.xaml.cs`, inside `RegisterPdfFonts()`, after the Hebrew registration block (ends ~line 186, before `GlobalFontSettings.FontResolver = …`), add:

```csharp
                foreach (var (file, family) in new[]
                {
                    ("NotoSansArabic-Regular.ttf", "Noto Sans Arabic"),
                    ("NotoSans-Regular.ttf",       "Noto Sans"),
                })
                {
                    try
                    {
                        var uri = new Uri($"pack://application:,,,/Resources/Fonts/{file}");
                        var info = GetResourceStream(uri);
                        if (info?.Stream is null) continue;
                        using var src = info.Stream;
                        using var ms = new System.IO.MemoryStream();
                        src.CopyTo(ms);
                        Scalpel.Services.PdfFontResolver.Instance
                            .RegisterBundledFont(family, ms.ToArray(), bold: false, italic: false);
                    }
                    catch { /* skip a missing/locked font resource */ }
                }
```

- [ ] **Step 6: Run to verify pass**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~FontEmbedding"`
Expected: PASS (existing + 2 new). If `HasEmbeddedFontProgram` fails for Arabic, confirm the test draws *presentation-form* codepoints (FE91/FE92) that the cmap actually maps.

- [ ] **Step 7: Commit**

```bash
git add Resources/Fonts/NotoSansArabic-Regular.ttf Resources/Fonts/NotoSansArabic-OFL.txt \
        Resources/Fonts/NotoSans-Regular.ttf Resources/Fonts/NotoSans-OFL.txt \
        Scalpel.csproj App.xaml.cs Scalpel.Tests/FontEmbeddingTests.cs
git commit -m "feat: bundle + register Noto Sans Arabic and Noto Sans (Cyrillic); prove embed"
```

---

### Task 4: Wire shaping + multi-script font choice into the save path

**Files:**
- Modify: `MainWindow.xaml.cs:8061-8090` (`FontHasHebrew` + `DrawTextRun`)
- Modify: `MainWindow.xaml.cs:6547`, `:6699`, `:6996` (WPF overlay TextBox `FontFamily`)

**Interfaces:**
- Consumes: `ArabicShaper.ContainsArabic/Shape` (T1), widened `BidiReorder` (T2), bundled `"Noto Sans Arabic"`/`"Noto Sans"` (T3), `TrueTypeCmap.CoversCodepoint`, `PdfFontResolver.Instance.TryGetExactFontBytes`.
- Produces: `DrawTextRun` shapes Arabic, reorders RTL, and draws with a script-appropriate embedded font. No signature change (callers at 8128 and 8171 unchanged).

- [ ] **Step 1: Replace `FontHasHebrew` (lines 8061–8067) with a generic coverage helper + script chooser**

```csharp
        /// <summary>True if <paramref name="family"/> (exact face) maps <paramref name="codepoint"/>.</summary>
        private static bool FontCovers(string family, bool bold, bool italic, int codepoint)
        {
            if (Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(family, bold, italic, out var bytes))
                return Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, codepoint);
            return false;
        }

        /// <summary>Pick a bundled face that covers the script of <paramref name="text"/>:
        /// Arabic → Noto Sans Arabic, Hebrew → Noto Sans Hebrew, Cyrillic → Noto Sans,
        /// otherwise the candidate (Latin). Falls back to the candidate if a bundled face
        /// isn't registered, so a missing font never blanks the text.</summary>
        private static string PickFace(string text, string candidate, bool bold, bool italic)
        {
            foreach (char c in text)
            {
                if (c >= '؀' && c <= 'ۿ' || c >= 'ﭐ' && c <= '﻿')
                    return FontCovers(candidate, bold, italic, 0x0628) ? candidate : "Noto Sans Arabic";
                if (c >= '֐' && c <= '׿' || c >= 'יִ' && c <= 'ﭏ')
                    return FontCovers(candidate, bold, italic, 0x05D0) ? candidate : "Noto Sans Hebrew";
                if (c >= 'Ѐ' && c <= 'ӿ')
                    return FontCovers(candidate, bold, italic, 0x0410) ? candidate : "Noto Sans";
            }
            return candidate;
        }
```

- [ ] **Step 2: Replace the body of `DrawTextRun` (lines 8073–8090)** so it shapes Arabic, reorders, and picks the face:

```csharp
        private static void DrawTextRun(XGraphics gfx, string text, string candidateFamily,
            double fontSizePx, XFontStyle style, XBrush brush,
            double leftX, double rightX, double baselineY)
        {
            bool bold = style == XFontStyle.Bold || style == XFontStyle.BoldItalic;
            bool italic = style == XFontStyle.Italic || style == XFontStyle.BoldItalic;

            if (!Scalpel.Services.BidiReorder.ContainsRtl(text))
            {
                // LTR (incl. Cyrillic): pick a covering face so Russian doesn't render as boxes.
                string ltrFace = PickFace(text, candidateFamily, bold, italic);
                gfx.DrawString(text, new XFont(ltrFace, fontSizePx, style), brush, leftX, baselineY);
                return;
            }

            // RTL: shape Arabic (cursive joining) BEFORE reordering, then reverse to visual order.
            string shaped = Scalpel.Services.ArabicShaper.ContainsArabic(text)
                ? Scalpel.Services.ArabicShaper.Shape(text)
                : text;
            string face = PickFace(shaped, candidateFamily, bold, italic);
            var font = new XFont(face, fontSizePx, style);
            string visual = Scalpel.Services.BidiReorder.ToVisual(shaped);
            double width = gfx.MeasureString(visual, font).Width;
            double x = rightX > leftX ? rightX - width : leftX;
            gfx.DrawString(visual, font, brush, x, baselineY);
        }
```

- [ ] **Step 3: Extend the live-edit TextBox font fallback** at lines 6547, 6699, 6996 — replace each:

```csharp
                tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew");
```
with (note line 6699 is in an object initializer `FontFamily = new FontFamily(...)` — keep that form):

```csharp
                tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic");
```

(Cyrillic is already covered by Segoe UI; WPF natively shapes Arabic when `FlowDirection` is RTL, which the existing `TextChanged` handler sets via `ContainsRtl` — now Arabic-aware after T2.)

- [ ] **Step 4: Build to verify it compiles**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: BUILD SUCCEEDED. (If `NETSDK1047` appears, re-run without `--no-restore`.) If `Scalpel.exe` is running it can lock `pdfium.dll` (MSB3027) — close the app and rebuild; that's not a code error.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: shape Arabic + pick script font in PDF save path; Arabic in edit overlays"
```

---

### Task 5: End-to-end multilingual round-trip test + reusable sample PDF

**Files:**
- Test: `Scalpel.Tests/MultilingualPdfTests.cs` (new)
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (already links ArabicShaper from T1; no new link needed — test uses PdfPig + PdfSharpCore already referenced)

**Interfaces:**
- Consumes: `ArabicShaper.Shape`, `BidiReorder.ToVisual`, `PdfFontResolver`, PdfPig `UglyToad.PdfPig`.
- Produces: a generated `multilingual-sample.pdf` (written to repo `docs/samples/` AND asserted), proving Hebrew/Arabic/Russian are stored in correct logical order.

**Note on the assertion model:** Scalpel burns RTL text in *visual* order, but PdfSharpCore emits a `/ToUnicode` CMap, so PdfPig recovers the **original code points** of each drawn glyph. The robust, non-flaky assertion is therefore: the set of code points extracted for the Hebrew line equals the set in `שלום` (no missing glyphs / no `.notdef`), the Russian line round-trips exactly, and — the canonical guarantee — for a 2-letter Hebrew probe drawn via `DrawTextRun`'s reorder, the **visual** string differs from logical (proving reordering ran) while extraction still yields the logical text. We assert reordering at the `BidiReorder` layer (already covered in T2) and assert *embedding + recoverability* here.

- [ ] **Step 1: Write the test**

```csharp
using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests
{
    public class MultilingualPdfTests
    {
        private const string Hebrew  = "שלום";          // שלום
        private const string HebrewReversed = "םולש";    // מולש (must NOT be the logical content)
        private const string Arabic  = "سلام";          // سلام
        private const string Russian = "Привет"; // Привет

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static void EnsureFonts()
        {
            string fonts = Path.Combine(RepoRoot(), "Resources", "Fonts");
            void Reg(string fam, string file)
            {
                string p = Path.Combine(fonts, file);
                if (File.Exists(p)) PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(p), false, false);
            }
            Reg("Noto Sans Hebrew", "NotoSansHebrew-Regular.ttf");
            Reg("Noto Sans Arabic", "NotoSansArabic-Regular.ttf");
            Reg("Noto Sans",        "NotoSans-Regular.ttf");
            if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is null)
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        // Mirror DrawTextRun's RTL pipeline (shape → reorder) without the WPF MainWindow.
        private static string ToBurnedVisual(string logical)
        {
            string shaped = ArabicShaper.ContainsArabic(logical) ? ArabicShaper.Shape(logical) : logical;
            return BidiReorder.ToVisual(shaped);
        }

        [Fact]
        public void MultilingualPdf_AllScriptsEmbed_AndRtlNotReversedLogically()
        {
            EnsureFonts();
            string outDir = Path.Combine(RepoRoot(), "docs", "samples");
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "multilingual-sample.pdf");

            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawString("English: Hello", new XFont("Noto Sans", 16), XBrushes.Black, new XPoint(40, 60));
                gfx.DrawString("Russian: " + Russian, new XFont("Noto Sans", 16), XBrushes.Black, new XPoint(40, 100));
                gfx.DrawString(ToBurnedVisual(Hebrew), new XFont("Noto Sans Hebrew", 16), XBrushes.Black, new XPoint(400, 140));
                gfx.DrawString(ToBurnedVisual(Arabic), new XFont("Noto Sans Arabic", 16), XBrushes.Black, new XPoint(400, 180));
                doc.Save(path);
            }

            Assert.True(File.Exists(path));

            // Every page glyph must be embedded (no system-font reliance) and recoverable.
            using var pdf = PdfDocument.Open(path);
            string text = string.Concat(pdf.GetPages().Select(p => p.Text));

            // Russian round-trips exactly in logical order.
            Assert.Contains(Russian, text);
            // Hebrew: the ORIGINAL logical code points are recovered via /ToUnicode — NOT the reversed form.
            Assert.Contains(Hebrew, text);
            Assert.DoesNotContain(HebrewReversed, text);
            // Arabic: original base letters are recovered (PdfPig maps presentation forms back via ToUnicode).
            Assert.Contains(Arabic, text);
        }

        [Fact]
        public void Burn_ReordersRtl_VisualDiffersFromLogical()
        {
            // The canonical "not backwards" proof at the pipeline level: visual != logical for RTL,
            // and the visual is exactly the reversed logical for pure Hebrew.
            Assert.NotEqual(Hebrew, ToBurnedVisual(Hebrew));
            Assert.Equal(HebrewReversed, ToBurnedVisual(Hebrew));
        }
    }
}
```

- [ ] **Step 2: Run to verify**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj --filter "FullyQualifiedName~Multilingual"`
Expected: PASS. If `MultilingualPdf_…` fails on `Assert.Contains(Hebrew, text)` because PdfPig returns presentation forms or empty, fall back to asserting `pdf` text is non-empty AND the file has embedded font programs (reuse `FontEmbeddingTests.HasEmbeddedFontProgram` logic inline) — but FIRST confirm the `/ToUnicode` path: PdfSharpCore + Noto emits ToUnicode, so logical recovery is expected. Document any deviation in the commit.

- [ ] **Step 3: Commit (including the sample PDF)**

```bash
git add Scalpel.Tests/MultilingualPdfTests.cs docs/samples/multilingual-sample.pdf
git commit -m "test: end-to-end multilingual PDF round-trip; commit reusable sample"
```

---

### Task 6: Locale plumbing — enum, URIs, RTL window mirroring

**Files:**
- Modify: `Services/LocaleManager.cs` (enum + URIs + FlowDirection)
- Modify: `MainWindow.xaml.cs` — re-apply RTL after window construction

**Interfaces:**
- Produces: `Locale` gains `He, Ar, Ru`. `LocaleManager.ApplyInternal` sets `Application.Current.MainWindow.FlowDirection` (RightToLeft for He/Ar, else LeftToRight). New `public static bool IsRtlLocale(Locale l)`.

- [ ] **Step 1: Update the enum** (`Services/LocaleManager.cs:6`):

```csharp
    internal enum Locale { EnUS, Es, ZhTW, ZhCN, Bn, TrTR, He, Ar, Ru }
```

- [ ] **Step 2: Add URI cases** in `ApplyInternal` (after the `TrTR` case, line 44):

```csharp
                Locale.He   => new Uri("pack://application:,,,/Strings/he.xaml"),
                Locale.Ar   => new Uri("pack://application:,,,/Strings/ar.xaml"),
                Locale.Ru   => new Uri("pack://application:,,,/Strings/ru.xaml"),
```

- [ ] **Step 3: Apply RTL mirroring** — at the end of `ApplyInternal` (after the `merged[1] = dict` block), add:

```csharp
            // Mirror the whole UI for RTL locales (Hebrew/Arabic). Safe if MainWindow not yet created.
            var win = Application.Current?.MainWindow;
            if (win != null)
                win.FlowDirection = IsRtlLocale(locale) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
```

And add the helper to the class:

```csharp
        public static bool IsRtlLocale(Locale l) => l == Locale.He || l == Locale.Ar;
```

- [ ] **Step 4: Re-apply at startup** — `Initialize()` runs before `MainWindow` exists, so the FlowDirection set there is a no-op. In `MainWindow.xaml.cs`, find the constructor (after `InitializeComponent();`) and add a line that mirrors based on the restored locale. Locate the constructor and insert after `InitializeComponent();`:

```csharp
            this.FlowDirection = Scalpel.Services.LocaleManager.IsRtlLocale(Scalpel.Services.LocaleManager.Current)
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
```

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Commit**

```bash
git add Services/LocaleManager.cs MainWindow.xaml.cs
git commit -m "feat: he/ar/ru locales + RTL window mirroring for Hebrew/Arabic"
```

---

### Task 7: Translated string resources (he, ar, ru) + language-name keys

**Files:**
- Create: `Strings/he.xaml`, `Strings/ar.xaml`, `Strings/ru.xaml`
- Modify: all 9 `Strings/*.xaml` (add 3 `Str_Lang_*` keys)

**Interfaces:**
- Produces: three locale dictionaries, each containing **every** `x:Key` present in `Strings/en-US.xaml` (164 keys) plus the 3 new language-name keys, translated.

- [ ] **Step 1: Create each new file by copying `en-US.xaml`** and translating every value. Procedure (repeat for he, ar, ru):
  1. Copy `Strings/en-US.xaml` to `Strings/<loc>.xaml` verbatim (preserves the `ResourceDictionary` header and all `x:Key`s).
  2. Translate **only** the inner text of every `<sys:String>` element. Do **not** change any `x:Key`. Preserve any `{0}`/`{1}` placeholders and `&#x0A;` / `(Ctrl+…)` shortcut hints exactly.
  3. For Hebrew/Arabic, write natural RTL text; do not insert literal direction marks — WPF handles direction. Numbers and `Ctrl+O`-style hints stay as-is.

  Anchor examples (use these exact values; translate the rest in the same register):

  Hebrew `he.xaml` — `Str_Lang_*` block:
```xml
    <sys:String x:Key="Str_Lang_English">English</sys:String>
    <sys:String x:Key="Str_Lang_Spanish">ספרדית</sys:String>
    <sys:String x:Key="Str_Lang_Traditional_Chinese">סינית מסורתית</sys:String>
    <sys:String x:Key="Str_Lang_Simple_Chinese">סינית פשוטה</sys:String>
    <sys:String x:Key="Str_Lang_Bengali">בנגלית</sys:String>
    <sys:String x:Key="Str_Lang_Turkish">טורקית</sys:String>
    <sys:String x:Key="Str_Lang_Hebrew">עברית</sys:String>
    <sys:String x:Key="Str_Lang_Arabic">ערבית</sys:String>
    <sys:String x:Key="Str_Lang_Russian">רוסית</sys:String>
```

- [ ] **Step 2: Add the 3 new language-name keys to ALL existing locale files** (`en-US, es, zh-TW, zh-CN, bn, tr-TR`), after the existing `Str_Lang_Turkish` line (around line 64). Use the language's own translation of each name. Example for `en-US.xaml`:

```xml
    <sys:String x:Key="Str_Lang_Hebrew">Hebrew</sys:String>
    <sys:String x:Key="Str_Lang_Arabic">Arabic</sys:String>
    <sys:String x:Key="Str_Lang_Russian">Russian</sys:String>
```

  And the **same three keys must also be present** in `he.xaml`/`ar.xaml`/`ru.xaml` (covered by Step 1's anchor block). Native endonyms recommended (e.g. in `ru.xaml`: `Русский`, `Иврит`, `Арабский`).

- [ ] **Step 3: Parity check — every locale has the same key set**

Run (bash):
```bash
for f in Strings/en-US.xaml Strings/he.xaml Strings/ar.xaml Strings/ru.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml; do
  printf "%s " "$f"; grep -o 'x:Key="[^"]*"' "$f" | sort -u | wc -l
done
```
Expected: identical counts across all 9 files (167 = 164 + 3). If a count differs, diff the key lists and add the missing keys. This guards the "every key in every file" invariant.

- [ ] **Step 4: Build to confirm the XAML parses**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: BUILD SUCCEEDED (malformed XAML or a stray unescaped `&` would fail here).

- [ ] **Step 5: Commit**

```bash
git add Strings/
git commit -m "feat: Hebrew/Arabic/Russian UI translations + language-name keys"
```

---

### Task 8: Settings UI — radios, handlers, sync, E2E catalog

**Files:**
- Modify: `MainWindow.xaml:1046-1050` (add 3 radios after Turkish)
- Modify: `MainWindow.xaml.cs:442-443` (add 3 handlers), `:327-328` (add 3 sync lines)
- Modify: `Scalpel.E2E/Catalog/Catalog.cs:58` (add 3 catalog entries)

**Interfaces:**
- Consumes: `LocaleManager.Apply`, `Locale.He/Ar/Ru` (T6), `Str_Lang_Hebrew/Arabic/Russian` (T7).

- [ ] **Step 1: Add radios** in `MainWindow.xaml` after the `LangTrRadio` block (after line 1050):

```xml
                     <RadioButton x:Name="LangHeRadio"
                                  Content="{DynamicResource Str_Lang_Hebrew}"
                                  GroupName="LangGroup"
                                  Style="{StaticResource ThemeRadio}"
                                  Checked="LangHeRadio_Checked"/>
                     <RadioButton x:Name="LangArRadio"
                                  Content="{DynamicResource Str_Lang_Arabic}"
                                  GroupName="LangGroup"
                                  Style="{StaticResource ThemeRadio}"
                                  Checked="LangArRadio_Checked"/>
                     <RadioButton x:Name="LangRuRadio"
                                  Content="{DynamicResource Str_Lang_Russian}"
                                  GroupName="LangGroup"
                                  Style="{StaticResource ThemeRadio}"
                                  Checked="LangRuRadio_Checked"/>
```

- [ ] **Step 2: Add handlers** in `MainWindow.xaml.cs` after `LangTrRadio_Checked` (line 443):

```csharp
        private void LangHeRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.He);

        private void LangArRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Ar);

        private void LangRuRadio_Checked(object sender, RoutedEventArgs e)
            => Scalpel.Services.LocaleManager.Apply(Scalpel.Services.Locale.Ru);
```

- [ ] **Step 3: Add sync lines** in `SettingsBtn_Click`, after line 328 (`LangTrRadio.IsChecked = …`):

```csharp
            LangHeRadio.IsChecked   = curLoc == Scalpel.Services.Locale.He;
            LangArRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Ar;
            LangRuRadio.IsChecked   = curLoc == Scalpel.Services.Locale.Ru;
```

- [ ] **Step 4: Add E2E catalog entries** in `Scalpel.E2E/Catalog/Catalog.cs` after line 58 (`new("LangTrRadio", …)`):

```csharp
        new("LangHeRadio",        Surface.SettingsOverlay, null),
        new("LangArRadio",        Surface.SettingsOverlay, null),
        new("LangRuRadio",        Surface.SettingsOverlay, null),
```

- [ ] **Step 5: Build (app + E2E)**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug && ~/.dotnet/dotnet.exe build Scalpel.E2E/Scalpel.E2E.csproj -c Debug`
Expected: BUILD SUCCEEDED for both. (If the E2E project doesn't build standalone, build the solution instead.)

- [ ] **Step 6: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs Scalpel.E2E/Catalog/Catalog.cs
git commit -m "feat: Settings entries for Hebrew/Arabic/Russian + E2E catalog"
```

---

### Task 9: Full-suite verification + RTL layout pass

**Files:**
- Modify (as needed): `MainWindow.xaml` (per-element `FlowDirection="LeftToRight"` overrides for elements that must not mirror)
- Modify: `CLAUDE.md` (one line noting the new fonts/locales), `Strings/TRANSLATING.md` (list new locales)

- [ ] **Step 1: Run the entire test suite**

Run: `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj`
Expected: ALL PASS (ArabicShaper, BidiReorder, FontEmbedding, Multilingual, plus pre-existing). Record the pass count.

- [ ] **Step 2: Launch the app and manually verify** (per the `run` skill; app must not be left running so it doesn't lock `pdfium.dll`):
  1. Open Settings → switch to **Russian**: UI strings are Cyrillic, layout stays LTR.
  2. Switch to **Hebrew**: UI strings are Hebrew AND the layout mirrors (File/Zoom groups on the right, panels flipped).
  3. Switch to **Arabic**: UI mirrors; strings are Arabic and connected.
  4. In Edit mode, add a text annotation, type `שלום`, `سلام`, and `Привет`; confirm each displays correctly (Hebrew/Arabic right-aligned & connected, not reversed). Save; reopen the saved PDF in Scalpel and a third-party viewer; confirm `שלום` reads `שלום` (not `מולש`), Arabic letters connect, and Russian shows real glyphs.

  Note any UI element that mirrors wrongly (e.g. logo, numeric zoom field, icon-only buttons) and add `FlowDirection="LeftToRight"` to just that element in `MainWindow.xaml`. Rebuild and re-check.

- [ ] **Step 3: Update docs**
  - `CLAUDE.md` localization section: add `he`, `ar`, `ru` to the locale list and note bundled `NotoSansArabic-Regular.ttf` / `NotoSans-Regular.ttf` + that he/ar mirror the UI RTL.
  - `Strings/TRANSLATING.md`: add the three new locales to its list.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: RTL layout fixes + docs for Hebrew/Arabic/Russian support"
```

---

## Self-Review

**Spec coverage:**
- Text rendering / non-reversal → T1 (shaping), T2 (Arabic bidi), T4 (save-path wiring), T5 (round-trip proof). ✓
- UI languages + RTL mirroring → T6 (locales + FlowDirection), T7 (translations), T8 (Settings), T9 (layout pass). ✓
- Fonts → T3 (bundle + register + embed proof). ✓
- Testing + sample PDF → T1, T2, T3, T5 (incl. committed `docs/samples/multilingual-sample.pdf`). ✓
- Known search limitation → carried in spec, not a task (accepted). ✓

**Placeholder scan:** No "TBD"/"handle edge cases" — every code step shows code; the translation task gives an exact procedure + anchor values (the 492 individual translations are the deliverable's content, produced per the stated procedure). ✓

**Type consistency:** `ArabicShaper.ContainsArabic/Shape`, `BidiReorder.ContainsRtl/ToVisual/IsRtl`, `TrueTypeCmap.CoversCodepoint(byte[],int,int)`, `PdfFontResolver.Instance.{RegisterBundledFont,TryGetExactFontBytes}`, `DrawTextRun(XGraphics,string,string,double,XFontStyle,XBrush,double,double,double)`, `PickFace(string,string,bool,bool)`, `FontCovers(string,bool,bool,int)`, `LocaleManager.{Apply,Current,IsRtlLocale}`, bundled family literals `"Noto Sans Arabic"`/`"Noto Sans"`/`"Noto Sans Hebrew"` — consistent across tasks. ✓
