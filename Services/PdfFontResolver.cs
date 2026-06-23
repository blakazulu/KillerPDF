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

        // Required by IFontResolver in PdfSharpCore 1.3.67 — the fallback face name
        // returned when resolution fails. Spike serves only Arial so we return that.
        public string DefaultFontName => "Arial";

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
