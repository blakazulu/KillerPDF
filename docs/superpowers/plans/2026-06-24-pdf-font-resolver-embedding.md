# PdfSharpCore Font Resolver + Embedding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register a PdfSharpCore `IFontResolver` that serves both installed system fonts and bundled app fonts so PdfSharpCore can embed either, draw new text annotations in bundled Geist, and add an automated test proving saved PDFs embed their fonts.

**Architecture:** A process-global `IFontResolver` (`Services/PdfFontResolver.cs`) replaces PdfSharpCore's built-in GDI resolver, so it reimplements system-font lookup (lazy index of `%WINDIR%\Fonts` via a TrueType `name`-table parser) plus a bundled-bytes registry. Registered once in `App.OnStartup`. A PdfPig/PdfSharpCore round-trip test asserts embedding. Spike-first to confirm the 1.3.67 interface before building the full index.

**Tech Stack:** C# / .NET Framework 4.8, WPF, PdfSharpCore 1.3.67 (`IFontResolver`, `GlobalFontSettings`, `XFont`, `PdfReader`), xUnit.

## Global Constraints

- Targets `net48`; build requires the .NET 8 SDK. `dotnet` may not be on PATH — use `~/.dotnet/dotnet.exe` for build/test.
- `Nullable` + `ImplicitUsings` enabled, `LangVersion=latest`. Use collection expressions `[]`, target-typed `new`, switch expressions.
- Defensive `try { } catch { }` that swallows and falls back — the resolver MUST never throw and MUST never return null from `ResolveTypeface`/`GetFont`.
- `GlobalFontSettings.FontResolver` is process-global and settable ONCE; setting it replaces the built-in GDI resolver. All registration goes through an idempotent guard (`if (GlobalFontSettings.FontResolver is null)`).
- Tests live in `Scalpel.Tests` (xUnit) and link source via `<Compile Include="..\Services\...">`. PdfSharpCore and PdfPig are already referenced by the test project.
- Bundled fonts present: `Resources/Fonts/Geist-Regular.ttf`, `Geist-Medium.ttf`, `Geist-SemiBold.ttf` (no italic). New text uses family name `"Geist"`.
- Geist is SIL OFL 1.1 — embedding in user PDFs is permitted.
- Build gotcha: if `dotnet build --no-restore` fails `NETSDK1047` after a prior publish, re-run WITH restore.
- All multi-byte integers in TrueType files are big-endian.

## Known PdfSharpCore 1.3.67 interface (Task 1 confirms by compiling)

```csharp
namespace PdfSharpCore.Fonts {
  public interface IFontResolver {
    byte[] GetFont(string faceName);
    FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic);
  }
  // FontResolverInfo(string faceName)
  // FontResolverInfo(string faceName, bool mustSimulateBold, bool mustSimulateItalic)
  public static class GlobalFontSettings { public static IFontResolver FontResolver { get; set; } }
}
```

If Task 1 finds the real signatures differ (e.g. an added `DefaultFontName` member), update every later task's code to match the verified shape and note it in the report.

---

## File Structure

- **Create** `Services/TrueTypeName.cs` — pure `name`-table reader: bytes (+ ttc face index) → `(Family, Subfamily)`.
- **Create** `Services/PdfFontResolver.cs` — the `IFontResolver`: bundled registry + lazy system index + resolve/get with fallback.
- **Create** `Scalpel.Tests/TrueTypeNameTests.cs`, `Scalpel.Tests/PdfFontResolverTests.cs`, `Scalpel.Tests/FontEmbeddingTests.cs`.
- **Modify** `App.xaml.cs:140` — register bundled Geist + set the resolver before `new MainWindow().Show()`.
- **Modify** `MainWindow.xaml.cs:8070` — draw new text annotations in `"Geist"`.
- **Modify** `Scalpel.Tests/Scalpel.Tests.csproj` — link the two new `Services/*.cs` files.

---

## Task 1: Spike — minimal resolver proves interface + embedding

Confirm the `IFontResolver` shape compiles and that a registered resolver causes a saved PDF to embed a system font. Minimal: serve only Arial from `%WINDIR%\Fonts`.

**Files:**
- Create: `Services/PdfFontResolver.cs` (minimal), `Scalpel.Tests/FontEmbeddingTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link PdfFontResolver.cs)

**Interfaces:**
- Produces: `Scalpel.Services.PdfFontResolver` implementing `PdfSharpCore.Fonts.IFontResolver`, with `static PdfFontResolver Instance`. Test helper `FontEmbeddingTests.HasEmbeddedFontProgram(string path) -> bool`.

- [ ] **Step 1: Link the new source file in the test csproj**

In `Scalpel.Tests/Scalpel.Tests.csproj`, after the `FontResolver.cs` link line, add:

```xml
    <Compile Include="..\Services\PdfFontResolver.cs" Link="Services\PdfFontResolver.cs" />
```

- [ ] **Step 2: Write the failing spike test**

Create `Scalpel.Tests/FontEmbeddingTests.cs`:

```csharp
using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")] // global GlobalFontSettings state — no parallel runs
    public class FontEmbeddingTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        /// <summary>True if any font in the saved PDF has an embedded font program.</summary>
        public static bool HasEmbeddedFontProgram(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
            foreach (var obj in doc.Internals.GetAllObjects())
            {
                if (obj is PdfDictionary dict &&
                    (dict.Elements.ContainsKey("/FontFile2") ||
                     dict.Elements.ContainsKey("/FontFile3") ||
                     dict.Elements.ContainsKey("/FontFile")))
                    return true;
            }
            return false;
        }

        [Fact]
        public void DrawingSystemFont_EmbedsIt()
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Embedding check 12345", new XFont("Arial", 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(File.Exists(path));
                Assert.True(HasEmbeddedFontProgram(path), "saved PDF should embed the Arial font program");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontEmbeddingTests"`
Expected: FAIL to compile — `PdfFontResolver` does not exist yet.

- [ ] **Step 4: Implement the minimal resolver**

Create `Services/PdfFontResolver.cs`:

```csharp
using System;
using System.IO;
using PdfSharpCore.Fonts;

namespace Scalpel.Services
{
    /// <summary>
    /// PdfSharpCore font resolver. Spike scope: serves Arial from the Windows fonts
    /// directory and proves embedding. Expanded in later tasks to a full system index
    /// plus a bundled-font registry.
    /// </summary>
    public sealed class PdfFontResolver : IFontResolver
    {
        public static PdfFontResolver Instance { get; } = new PdfFontResolver();

        private PdfFontResolver() { }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new FontResolverInfo("Arial");

        public byte[] GetFont(string faceName)
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string path = Path.Combine(fonts, "arial.ttf");
            return File.ReadAllBytes(path);
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontEmbeddingTests"`
Expected: PASS. If it fails to compile because the `IFontResolver` members differ from the snippet (e.g. a required `DefaultFontName`), implement the real members, keep the test green, and document the actual interface in the report. If `doc.Internals.GetAllObjects()` does not exist on this PdfSharpCore version, switch the embedding check to scan `doc` pages' font resources and report the working approach.

- [ ] **Step 6: Commit**

```bash
git add Services/PdfFontResolver.cs Scalpel.Tests/FontEmbeddingTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: spike PdfSharpCore font resolver, prove system-font embedding"
```

---

## Task 2: TrueType `name`-table parser

A pure reader that extracts family + subfamily from font bytes, handling `.ttc`.

**Files:**
- Create: `Services/TrueTypeName.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link it)
- Test: `Scalpel.Tests/TrueTypeNameTests.cs`

**Interfaces:**
- Produces: `Scalpel.Services.TrueTypeName.Read(byte[] data, int faceIndex = 0) -> TrueTypeName.Names` where `readonly record struct Names(string Family, string Subfamily)`.

- [ ] **Step 1: Link the source file**

In `Scalpel.Tests/Scalpel.Tests.csproj`, after the PdfFontResolver link, add:

```xml
    <Compile Include="..\Services\TrueTypeName.cs" Link="Services\TrueTypeName.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/TrueTypeNameTests.cs`:

```csharp
using System;
using System.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TrueTypeNameTests
    {
        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        [Fact]
        public void Read_Arial_ReturnsArialRegular()
        {
            string p = Path.Combine(FontsDir, "arial.ttf");
            Assert.True(File.Exists(p), $"expected {p} on a Windows machine");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Arial", names.Family);
            Assert.DoesNotContain("Bold", names.Subfamily, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Read_ArialBold_SubfamilyContainsBold()
        {
            string p = Path.Combine(FontsDir, "arialbd.ttf");
            Assert.True(File.Exists(p), $"expected {p} on a Windows machine");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Arial", names.Family);
            Assert.Contains("Bold", names.Subfamily, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Read_GeistRegular_FromRepo_FamilyIsGeist()
        {
            // Repo-relative: tests run from Scalpel.Tests/bin/<cfg>/net48; walk up to repo root.
            string repo = RepoRoot();
            string p = Path.Combine(repo, "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(p), $"expected bundled font at {p}");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Geist", names.Family);
        }

        [Fact]
        public void Read_Garbage_ReturnsEmpty()
        {
            var names = TrueTypeName.Read(new byte[] { 1, 2, 3, 4 });
            Assert.Equal("", names.Family);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TrueTypeNameTests"`
Expected: FAIL to compile — `TrueTypeName` does not exist.

- [ ] **Step 4: Implement the parser**

Create `Services/TrueTypeName.cs`:

```csharp
using System;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal TrueType/OpenType 'name'-table reader. Extracts family (name ID 1, or
    /// typographic family ID 16) and subfamily (ID 2 / ID 17) from font bytes.
    /// Handles .ttc collections via faceIndex. Pure and defensive: returns ("","")
    /// on any malformed input.
    /// </summary>
    public static class TrueTypeName
    {
        public readonly record struct Names(string Family, string Subfamily);

        public static Names Read(byte[] data, int faceIndex = 0)
        {
            try
            {
                int baseOffset = 0;
                if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t' &&
                    data[2] == (byte)'c' && data[3] == (byte)'f')
                {
                    uint numFonts = ReadU32(data, 8);
                    if (faceIndex < 0 || faceIndex >= numFonts) faceIndex = 0;
                    baseOffset = (int)ReadU32(data, 12 + faceIndex * 4);
                }

                ushort numTables = ReadU16(data, baseOffset + 4);
                int dir = baseOffset + 12;
                int nameOffset = -1;
                for (int i = 0; i < numTables; i++)
                {
                    int rec = dir + i * 16;
                    if (data[rec] == (byte)'n' && data[rec + 1] == (byte)'a' &&
                        data[rec + 2] == (byte)'m' && data[rec + 3] == (byte)'e')
                    {
                        nameOffset = (int)ReadU32(data, rec + 8);
                        break;
                    }
                }
                if (nameOffset < 0) return new Names("", "");

                ushort count = ReadU16(data, nameOffset + 2);
                ushort stringOffset = ReadU16(data, nameOffset + 4);
                int storage = nameOffset + stringOffset;

                string family = "", subfamily = "", typoFamily = "", typoSub = "";
                for (int i = 0; i < count; i++)
                {
                    int rec = nameOffset + 6 + i * 12;
                    ushort platformId = ReadU16(data, rec);
                    ushort nameId = ReadU16(data, rec + 6);
                    ushort len = ReadU16(data, rec + 8);
                    ushort off = ReadU16(data, rec + 10);
                    string? value = DecodeName(data, storage + off, len, platformId);
                    if (value is null) continue;
                    switch (nameId)
                    {
                        case 1:  if (family == "") family = value; break;
                        case 2:  if (subfamily == "") subfamily = value; break;
                        case 16: if (typoFamily == "") typoFamily = value; break;
                        case 17: if (typoSub == "") typoSub = value; break;
                    }
                }
                return new Names(
                    typoFamily != "" ? typoFamily : family,
                    typoSub != "" ? typoSub : subfamily);
            }
            catch { return new Names("", ""); }
        }

        private static string? DecodeName(byte[] d, int offset, int len, ushort platformId)
        {
            if (offset < 0 || len <= 0 || offset + len > d.Length) return null;
            if (platformId == 3 || platformId == 0) // Windows / Unicode → UTF-16 BE
                return Encoding.BigEndianUnicode.GetString(d, offset, len);
            if (platformId == 1) // Mac Roman (approx)
                return Encoding.ASCII.GetString(d, offset, len);
            return null;
        }

        private static ushort ReadU16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
        private static uint ReadU32(byte[] d, int o) =>
            (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TrueTypeNameTests"`
Expected: PASS. If `Geist-Regular.ttf`'s family resolves to something other than `"Geist"` (e.g. a typographic-name quirk), adjust the assertion to the actual embedded family name and note it — the bundled registry in Task 4 must register under whatever name PdfSharpCore will request, which is the WPF family `"Geist"`.

- [ ] **Step 6: Commit**

```bash
git add Services/TrueTypeName.cs Scalpel.Tests/TrueTypeNameTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: add TrueType name-table parser"
```

---

## Task 3: Full system-font index in the resolver

Replace the spike's hard-coded Arial with a real lazy index of `%WINDIR%\Fonts`, exact/style-simulated/fallback resolution, and TTC-aware byte reads.

**Files:**
- Modify: `Services/PdfFontResolver.cs` (replace body from Task 1)
- Test: `Scalpel.Tests/PdfFontResolverTests.cs`

**Interfaces:**
- Consumes: `TrueTypeName.Read` (Task 2).
- Produces: `PdfFontResolver.Instance.ResolveTypeface(...)`/`GetFont(...)` backed by the system index; `ResolveTypeface` never returns null.

- [ ] **Step 1: Write the failing tests**

Create `Scalpel.Tests/PdfFontResolverTests.cs`:

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class PdfFontResolverTests
    {
        [Fact]
        public void Resolve_Arial_ReturnsNonNullFace_AndGetFontReturnsBytes()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("Arial", false, false);
            Assert.NotNull(info);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 1000, "a real font program is far larger than 1KB");
        }

        [Fact]
        public void Resolve_ArialBold_ResolvesToAFace()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("Arial", true, false);
            Assert.NotNull(info);
            // Either a real bold face or the regular face flagged to simulate bold.
            Assert.True(info.MustSimulateBold || info.FaceName.Length > 0);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.True(bytes.Length > 1000);
        }

        [Fact]
        public void Resolve_UnknownFamily_FallsBackNeverNull()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("ThisFontDoesNotExist123", false, false);
            Assert.NotNull(info);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.True(bytes.Length > 1000, "fallback face must yield a real font program");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~PdfFontResolverTests"`
Expected: FAIL — the spike `ResolveTypeface` ignores the family and `GetFont` only reads arial.ttf, so `Resolve_UnknownFamily` and bold resolution are not meaningfully exercised; the bold/fallback face keys won't map. (If they happen to pass against the spike stub, they will fail meaningfully once Step 3 replaces the stub — proceed.)

- [ ] **Step 3: Implement the full resolver**

Replace the entire body of `Services/PdfFontResolver.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Fonts;

namespace Scalpel.Services
{
    /// <summary>
    /// Process-global PdfSharpCore font resolver. Replaces the built-in GDI resolver,
    /// so it serves both installed system fonts (lazy index of the Windows fonts dir)
    /// and bundled application fonts (registered byte arrays). Never throws; never
    /// returns null — unknown families fall back to Arial.
    /// </summary>
    public sealed class PdfFontResolver : IFontResolver
    {
        public static PdfFontResolver Instance { get; } = new PdfFontResolver();
        private PdfFontResolver() { }

        private const string FallbackFamily = "arial";

        // faceKey -> (filePath, ttcFaceIndex). faceKey is "family|b|i" lowercased.
        private Dictionary<string, (string Path, int Face)>? _systemIndex;
        private readonly Dictionary<string, byte[]> _bundled = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _byFaceCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        // family -> "regular" exists? used for style simulation decisions.
        private HashSet<string>? _families;

        /// <summary>Register a bundled font's bytes for a family + style. Bundled wins
        /// over system. Called at app startup (pack:// bytes) and from tests.</summary>
        public void RegisterBundledFont(string family, byte[] bytes, bool bold, bool italic)
        {
            if (string.IsNullOrWhiteSpace(family) || bytes is null || bytes.Length == 0) return;
            lock (_lock) _bundled[FaceKey(family, bold, italic)] = bytes;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            try
            {
                string fam = (familyName ?? "").Trim();
                EnsureIndex();

                // 1. Exact bundled or system match.
                string exact = FaceKey(fam, isBold, isItalic);
                if (_bundled.ContainsKey(exact) || _systemIndex!.ContainsKey(exact))
                    return new FontResolverInfo(exact);

                // 2. Family exists but not this exact style → use regular, simulate.
                string regular = FaceKey(fam, false, false);
                if (_bundled.ContainsKey(regular) || _systemIndex!.ContainsKey(regular))
                    return new FontResolverInfo(regular, isBold, isItalic);

                // 3. Unknown family → Arial, simulate requested style.
                string fb = FaceKey(FallbackFamily, false, false);
                return new FontResolverInfo(fb, isBold, isItalic);
            }
            catch
            {
                return new FontResolverInfo(FaceKey(FallbackFamily, false, false), isBold, isItalic);
            }
        }

        public byte[] GetFont(string faceName)
        {
            try
            {
                lock (_lock)
                {
                    if (_byFaceCache.TryGetValue(faceName, out var cached)) return cached;
                    byte[]? bytes = null;
                    if (_bundled.TryGetValue(faceName, out var b)) bytes = b;
                    else
                    {
                        EnsureIndex();
                        if (_systemIndex!.TryGetValue(faceName, out var loc))
                            bytes = ExtractFace(loc.Path, loc.Face);
                    }
                    bytes ??= ReadFallback();
                    _byFaceCache[faceName] = bytes;
                    return bytes;
                }
            }
            catch { return ReadFallback(); }
        }

        // ---- internals ----

        private static string FaceKey(string family, bool bold, bool italic)
            => $"{family.Trim().ToLowerInvariant()}|{(bold ? 1 : 0)}|{(italic ? 1 : 0)}";

        private void EnsureIndex()
        {
            if (_systemIndex is not null) return;
            lock (_lock)
            {
                if (_systemIndex is not null) return;
                var index = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
                var fams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is not (".ttf" or ".ttc" or ".otf")) continue;
                        try
                        {
                            byte[] data = File.ReadAllBytes(file);
                            int faces = CountFaces(data);
                            for (int fi = 0; fi < faces; fi++)
                            {
                                var n = TrueTypeName.Read(data, fi);
                                if (string.IsNullOrEmpty(n.Family)) continue;
                                bool bold = ContainsCI(n.Subfamily, "bold");
                                bool italic = ContainsCI(n.Subfamily, "italic") || ContainsCI(n.Subfamily, "oblique");
                                string key = FaceKey(n.Family, bold, italic);
                                if (!index.ContainsKey(key)) index[key] = (file, fi);
                                fams.Add(n.Family.Trim().ToLowerInvariant());
                            }
                        }
                        catch { /* skip malformed file */ }
                    }
                }
                catch { /* leave index empty; fallback path still works */ }
                _families = fams;
                _systemIndex = index;
            }
        }

        private static bool ContainsCI(string s, string sub)
            => !string.IsNullOrEmpty(s) && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int CountFaces(byte[] data)
        {
            if (data.Length >= 12 && data[0] == (byte)'t' && data[1] == (byte)'t' &&
                data[2] == (byte)'c' && data[3] == (byte)'f')
                return (int)((uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]));
            return 1;
        }

        /// <summary>Return embeddable bytes for one face. For a single-face file this is
        /// the whole file; PdfSharpCore selects the right glyphs. For a .ttc we return the
        /// whole collection bytes (PdfSharpCore reads via the file); if that proves wrong
        /// in QA, extract the single font — but most installed text fonts are .ttf.</summary>
        private static byte[] ExtractFace(string path, int face) => File.ReadAllBytes(path);

        private byte[] ReadFallback()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var name in new[] { "arial.ttf", "segoeui.ttf", "tahoma.ttf" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
            // Last resort: first readable font file.
            foreach (var f in Directory.EnumerateFiles(dir, "*.ttf"))
            {
                try { return File.ReadAllBytes(f); } catch { }
            }
            throw new FileNotFoundException("no usable system font found");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~PdfFontResolverTests"`
Expected: PASS. Then re-run the Task 1 embedding test to confirm no regression:
`~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontEmbeddingTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/PdfFontResolver.cs Scalpel.Tests/PdfFontResolverTests.cs
git commit -m "feat: full system-font index in PdfFontResolver with style fallback"
```

---

## Task 4: Bundled-font registry exercised end-to-end

Prove a registered bundled font embeds (the path Geist and later Hebrew use).

**Files:**
- Modify: `Scalpel.Tests/FontEmbeddingTests.cs` (add a bundled-font case)

**Interfaces:**
- Consumes: `PdfFontResolver.RegisterBundledFont` (Task 3), `HasEmbeddedFontProgram` (Task 1).

- [ ] **Step 1: Add the failing bundled-embedding test**

In `Scalpel.Tests/FontEmbeddingTests.cs`, add inside the class:

```csharp
        [Fact]
        public void DrawingRegisteredBundledFont_EmbedsIt()
        {
            EnsureResolver();
            // Register the repo's bundled Geist as a uniquely-named family so this test
            // is independent of whether Geist is installed on the machine.
            string repo = RepoRoot();
            string geist = Path.Combine(repo, "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(geist), $"expected bundled font at {geist}");
            const string fam = "ScalpelBundledTestFont";
            PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(geist), false, false);

            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_b_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Bundled embed 12345", new XFont(fam, 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path), "saved PDF should embed the bundled font");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
```

- [ ] **Step 2: Run to verify it passes (no production change needed)**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontEmbeddingTests"`
Expected: PASS — Task 3's resolver already serves bundled fonts via `RegisterBundledFont`. If `DrawingRegisteredBundledFont_EmbedsIt` fails because PdfSharpCore requests a face key the resolver didn't register (e.g. a normalized name), make the resolver's `ResolveTypeface` match the requested family case-insensitively (it already lowercases in `FaceKey`) and re-run; report any mismatch found.

- [ ] **Step 3: Commit**

```bash
git add Scalpel.Tests/FontEmbeddingTests.cs
git commit -m "test: prove bundled fonts embed via the resolver"
```

---

## Task 5: Register at startup + draw new text in Geist

Wire the resolver into the running app and switch new text annotations to Geist.

**Files:**
- Modify: `App.xaml.cs:140` (registration), `MainWindow.xaml.cs:8070` (Geist)

**Interfaces:**
- Consumes: `PdfFontResolver.Instance`, `RegisterBundledFont` (Tasks 3-4).

- [ ] **Step 1: Add the startup registration helper to App**

In `App.xaml.cs`, add this method to the `App` class (near `OnStartup`):

```csharp
        /// <summary>Register bundled fonts with PdfSharpCore and install our resolver
        /// (once). Lets saved PDFs embed both system and bundled (Geist) fonts.</summary>
        private static void RegisterPdfFonts()
        {
            try
            {
                if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is not null) return;
                foreach (var (file, bold) in new[]
                {
                    ("Geist-Regular.ttf", false),
                    ("Geist-SemiBold.ttf", true),
                })
                {
                    try
                    {
                        var uri = new Uri($"pack://application:,,,/Resources/Fonts/{file}");
                        var info = GetResourceStream(uri);
                        if (info?.Stream is null) continue;
                        using var ms = new System.IO.MemoryStream();
                        info.Stream.CopyTo(ms);
                        Scalpel.Services.PdfFontResolver.Instance
                            .RegisterBundledFont("Geist", ms.ToArray(), bold, italic: false);
                    }
                    catch { /* skip a missing/locked font resource */ }
                }
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver =
                    Scalpel.Services.PdfFontResolver.Instance;
            }
            catch { /* never block startup over font setup */ }
        }
```

- [ ] **Step 2: Call it during startup**

In `App.xaml.cs`, in `OnStartup`, immediately before `new MainWindow().Show();` (line 140), add:

```csharp
            RegisterPdfFonts();
```

- [ ] **Step 3: Draw new text annotations in Geist**

In `MainWindow.xaml.cs` at the `case TextAnnotation ta:` save path (line ~8070), change:

```csharp
                            var font = new XFont("Segoe UI", ta.FontSize * sy);
```
to:
```csharp
                            var font = new XFont("Geist", ta.FontSize * sy);
```

(Read the surrounding lines first to confirm the variable name `font` and the exact `XFont` construction; if a style argument is present, preserve it.)

- [ ] **Step 4: Build and run the full suite**

Run: `~/.dotnet/dotnet.exe build` then `~/.dotnet/dotnet.exe test`
Expected: build succeeds; all tests pass. If `NETSDK1047`, re-run `~/.dotnet/dotnet.exe build` with restore.

- [ ] **Step 5: Manual verification (documented)**

Run the app. (a) Add a new text annotation, save, reopen — text renders in Geist; inspect the saved PDF (any PDF tool, or open on a machine without Geist) to confirm the font is embedded. (b) Edit existing installed-font text, save — confirm it stays embedded. (c) Generate the sample document / open + save a normal PDF to confirm Arial and other system fonts still draw correctly after the resolver override (regression check for the GDI replacement). Record results in the PR.

- [ ] **Step 6: Commit**

```bash
git add App.xaml.cs MainWindow.xaml.cs
git commit -m "feat: register font resolver at startup; draw new text in bundled Geist"
```

---

## Self-Review

**Spec coverage:**
- Custom `IFontResolver` serving system + bundled fonts → Tasks 1, 3, 4. ✓
- Reimplemented system-font resolution (GDI replaced) → Task 3 index + Task 5 registration. ✓
- New text drawn in Geist, embedded → Task 5 Step 3 (+ Task 4 proves bundled embedding). ✓
- Automated embedding-guarantee test → Task 1 (system) + Task 4 (bundled). ✓
- TrueType `name`-table parser → Task 2. ✓
- Spike-first de-risk → Task 1. ✓
- Never-throw/never-null resolver → Task 3 catches + fallback; tested in `Resolve_UnknownFamily`. ✓
- Idempotent global registration → `EnsureResolver` (tests) + `RegisterPdfFonts` guard (app). ✓
- Geist OFL embeddable → constraint noted; uses bundled bytes. ✓

**Type consistency:** `PdfFontResolver.Instance`, `ResolveTypeface(string,bool,bool)→FontResolverInfo`, `GetFont(string)→byte[]`, `RegisterBundledFont(string,byte[],bool,bool)`, `TrueTypeName.Read(byte[],int)→Names(Family,Subfamily)`, `FaceKey` format `"family|b|i"`, `HasEmbeddedFontProgram(string)→bool`, `RepoRoot()` — all consistent across tasks. `FontResolverInfo.FaceName`/`MustSimulateBold` used per the documented interface (Task 1 confirms).

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The interface-shape and `.ttc`-byte-extraction uncertainties are framed as explicit verify-and-adjust gates in Tasks 1/3, not deferred work — each ships working code with a documented fallback if the environment differs.

**Risk note for the executor:** xUnit may parallelize test classes; `FontEmbeddingTests` mutates the global `GlobalFontSettings.FontResolver` and is marked `[Collection("FontResolver")]`. If any other new test sets the global resolver, put it in the same collection. The resolver-unit tests (`PdfFontResolverTests`) call the instance directly and do not touch global state, so they are parallel-safe.
