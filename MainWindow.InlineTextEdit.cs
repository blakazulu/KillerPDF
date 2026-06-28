using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Inline text editing (double-click)
        // ============================================================

        private void EditTextAtPosition(Point canvasPos, int pageIdx)
        {
            if (_currentFile is null || !_renderDims.ContainsKey(pageIdx)) return;

            // Commit any existing edit first
            if (_activeTextBox is not null)
            {
                CommitActiveTextBox();
                return;
            }

            // Re-edit an already-committed TextEditAnnotation without re-reading the PDF.
            // Without this check, a second double-click would read the original file, produce
            // a duplicate whiteout+text layer, and cause the "overlapping quasi-duplicates" bug.
            if (_annotations.TryGetValue(pageIdx, out var existingPage))
            {
                var existingEdit = existingPage.OfType<TextEditAnnotation>()
                    .FirstOrDefault(a => a.OriginalBounds.Contains(canvasPos));
                if (existingEdit is not null)
                {
                    var reb = existingEdit.OriginalBounds;
                    var retb = new TextBox
                    {
                        Text = existingEdit.NewContent,
                        Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                        Foreground = Brushes.Black,
                        BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                        BorderThickness = new Thickness(2),
                        FontFamily = new FontFamily(existingEdit.FontName),
                        FontSize = Math.Max(existingEdit.FontSize, 10),
                        FontWeight = ToWeight(existingEdit.IsBold),
                        FontStyle = ToStyle(existingEdit.IsItalic),
                        FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(existingEdit.NewContent)
                            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        MinWidth = Math.Max(reb.Width + 20, 100),
                        // Height from the font size so the box fits the text at any size
                        // (see EditTextAtPosition new-edit path for the rationale).
                        Height = Math.Max(Math.Max(existingEdit.FontSize, 10) * 1.35 + 6, 24),
                        Padding = new Thickness(2, 0, 2, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        AcceptsReturn = false,
                        Tag = new TextEditContext
                        {
                            PageIndex = pageIdx,
                            OriginalText = existingEdit.OriginalContent,
                            CanvasBounds = reb,
                            Position = existingEdit.Position,
                            FontSize = existingEdit.FontSize,
                            FontName = existingEdit.FontName,
                            IsBold = existingEdit.IsBold,
                            IsItalic = existingEdit.IsItalic,
                            // Carry the embedded font forward so re-edits re-gate against the new text
                            // (bytes are fetched from the resolver at commit via this key).
                            EmbeddedFamilyKey = existingEdit.ExactFontFamily,
                            ExistingAnnotation = existingEdit
                        }
                    };
                    Canvas.SetLeft(retb, reb.X);
                    Canvas.SetTop(retb, reb.Y);
                    _activeCanvas.Children.Add(retb);
                    _activeTextBox = retb;
                    var rewo = new Rectangle
                    {
                        Fill = Brushes.White,
                        Width = reb.Width + 4,
                        Height = reb.Height + 4,
                        IsHitTestVisible = false,
                        Tag = "EditWhiteout"
                    };
                    Canvas.SetLeft(rewo, reb.X - 2);
                    Canvas.SetTop(rewo, reb.Y - 2);
                    _activeCanvas.Children.Insert(_activeCanvas.Children.IndexOf(retb), rewo);
                    retb.KeyDown += EditTextBox_KeyDown;
                    retb.Loaded += (s, ev) => { retb.Focus(); Keyboard.Focus(retb); retb.SelectAll(); retb.LostFocus += EditTextBox_LostFocus; };
                    SetStatus("Re-editing text — Enter to save, Escape to cancel");
                    return;
                }
            }

            // Re-edit a user-placed text annotation: lift it into an editable box
            // pre-filled with its content, size (shown in points), and color.
            if (_annotations.TryGetValue(pageIdx, out var placedPage))
            {
                var placed = placedPage.OfType<TextAnnotation>()
                    .LastOrDefault(a => HitTestAnnotation(a, canvasPos, out _));
                if (placed is not null)
                {
                    var pcol = placed.GetColor();
                    _textColor = pcol;
                    double syp = 1.0;
                    if (_doc is not null && _renderDims.TryGetValue(pageIdx, out var prd) && prd.h > 0)
                        syp = _doc.Pages[pageIdx].Height.Point / prd.h;
                    _textFontSize = Math.Max(1, Math.Round(placed.FontSize * syp));

                    _reeditOriginal = placed;
                    placedPage.Remove(placed);
                    RenderAllAnnotations(pageIdx);

                    var ptb = new TextBox
                    {
                        Text = placed.Content,
                        Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                        Foreground = new SolidColorBrush(pcol),
                        BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                        BorderThickness = new Thickness(1),
                        FontFamily = (FontFamily)FindResource("FontUI"),
                        FontSize = placed.FontSize,
                        MinWidth = 120,
                        MinHeight = 24,
                        Padding = new Thickness(2),
                        AcceptsReturn = true,
                        Tag = pageIdx
                    };
                    Canvas.SetLeft(ptb, placed.Position.X);
                    Canvas.SetTop(ptb, placed.Position.Y);
                    _activeCanvas.Children.Add(ptb);
                    _activeTextBox = ptb;
                    ptb.KeyDown += TextBox_KeyDown;
                    ptb.Loaded += (s, ev) => { ptb.Focus(); Keyboard.Focus(ptb); ptb.SelectAll(); ptb.LostFocus += TextBox_LostFocus; };
                    ShowTextSettings();
                    SetStatus("Editing text — change size/color above, Enter to save");
                    return;
                }
            }

            try
            {
                var (renderW, renderH) = _renderDims[pageIdx];

                using var pigDoc = PdfPigDoc.Open(_currentFile);
                if (pageIdx >= pigDoc.NumberOfPages) return;
                var page = pigDoc.GetPage(pageIdx + 1);

                double pdfW = page.Width;
                double pdfH = page.Height;
                double sxInv = (double)renderW / pdfW; // pdf->canvas
                double syInv = (double)renderH / pdfH;

                // Convert all words to canvas coordinates upfront
                var canvasWords = page.GetWords().Select(w =>
                {
                    double cx = w.BoundingBox.Left * sxInv;
                    double cy = renderH - (w.BoundingBox.Top * syInv);
                    double cw = (w.BoundingBox.Right - w.BoundingBox.Left) * sxInv;
                    double ch = (w.BoundingBox.Top - w.BoundingBox.Bottom) * syInv;
                    return new { Word = w, Rect = new Rect(cx, cy, cw, ch) };
                }).ToList();

                if (canvasWords.Count == 0) { SetStatus("No selectable text — this page may be a scanned image"); return; }

                // Find words on the same line as the click (Y overlap with tolerance)
                var clickY = canvasPos.Y;
                var lineWords = canvasWords
                    .Where(cw => clickY >= cw.Rect.Top - 3 && clickY <= cw.Rect.Bottom + 3)
                    .OrderBy(cw => cw.Rect.Left)  // strictly left-to-right
                    .ToList();

                if (lineWords.Count == 0)
                {
                    // Try nearest line within 20px
                    var nearest = canvasWords
                        .OrderBy(cw => Math.Abs((cw.Rect.Top + cw.Rect.Bottom) / 2 - clickY))
                        .First();
                    double nearMidY = (nearest.Rect.Top + nearest.Rect.Bottom) / 2;
                    lineWords = [..canvasWords
                        .Where(cw => Math.Abs((cw.Rect.Top + cw.Rect.Bottom) / 2 - nearMidY) < 5)
                        .OrderBy(cw => cw.Rect.Left)];
                }

                if (lineWords.Count == 0)
                {
                    SetStatus("No text line found at this position");
                    return;
                }

                // Compute bounding box in canvas space
                double cLeft = lineWords.Min(w => w.Rect.Left);
                double cTop = lineWords.Min(w => w.Rect.Top);
                double cRight = lineWords.Max(w => w.Rect.Right);
                double cBottom = lineWords.Max(w => w.Rect.Bottom);
                double cWidth = cRight - cLeft;
                double cHeight = cBottom - cTop;

                // Reconstruct the line in LOGICAL order. PdfPig returns words left-to-right, so a
                // Hebrew/Arabic line's words would join reversed; JoinWordsLogical walks RTL lines
                // right-to-left so the edit box (and the burned-in edit) reads correctly.
                string lineText = Scalpel.Services.BidiReorder.JoinWordsLogical(
                    [.. lineWords.Select(w => (w.Word.Text, w.Rect.Left))]);

                // Get actual font info from PdfPig letter data
                double canvasFontSize = cHeight * 0.75; // fallback
                string fontName = "Segoe UI"; // fallback
                bool isBold = false, isItalic = false;
                byte[]? embeddedBytes = null;   // the document's own font, when not installed
                string? embeddedKey = null;
                string fontDisplay = "";
                var firstWord = lineWords.First().Word;
                try
                {
                    if (firstWord.Letters.Count > 0)
                    {
                        var letter = firstWord.Letters[0];
                        // PointSize is the size as actually rendered (it accounts for text-matrix
                        // scaling); letter.FontSize is only the raw `Tf` size, which is often 1pt
                        // for matrix-scaled big text and would yield a tiny edit box. Prefer
                        // PointSize, fall back to FontSize, then to the measured glyph height.
                        double pdfFontPts = letter.PointSize > 0 ? letter.PointSize
                                          : letter.FontSize > 0 ? letter.FontSize
                                          : 0;
                        canvasFontSize = pdfFontPts > 0 ? pdfFontPts * syInv : cHeight * 0.9;

                        // Resolve raw PdfPig font name -> family + style + availability.
                        string? rawFont = null;
                        try { rawFont = letter.FontName; } catch { }
                        if (string.IsNullOrEmpty(rawFont))
                        {
                            try { rawFont = firstWord.FontName; } catch { }
                        }
                        var resolved = Scalpel.Services.FontResolver.Resolve(rawFont, AvailableFontFamilies());
                        fontName = resolved.FamilyName;
                        isBold = resolved.IsBold;
                        isItalic = resolved.IsItalic;
                        fontDisplay = resolved.DisplayName;
                        if (!resolved.IsInstalled)
                        {
                            // The font isn't installed. Try to use the DOCUMENT'S OWN embedded font so
                            // the edit looks identical. Usable only when the embedded program carries a
                            // Unicode cmap covering the line (subset CID fonts usually don't — then we
                            // fall back to a substitute and tell the user to install the font).
                            byte[]? emb = _currentFile is null ? null
                                : Scalpel.Services.EmbeddedFontExtractor.TryExtract(_currentFile, rawFont ?? resolved.DisplayName, out _);
                            if (emb is { Length: > 0 } && Scalpel.Services.TrueTypeCmap.CoversAllText(emb, lineText))
                            {
                                embeddedBytes = emb;
                                embeddedKey = "__emb_" + Scalpel.Services.EmbeddedFontExtractor.Normalize(resolved.DisplayName) + "_" + emb.Length;
                                Scalpel.Services.PdfFontResolver.Instance.RegisterBundledFont(embeddedKey, emb, isBold, isItalic);
                                // Exact font available — no toast.
                            }
                            else
                            {
                                ShowToast(string.Format(Loc("Str_FontMissing_Body"), resolved.DisplayName), resolved.DisplayName);
                            }
                        }
                    }
                }
                catch { /* use fallbacks */ }

                // Show editable TextBox over the line
                var tb = new TextBox
                {
                    Text = lineText,
                    Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                    Foreground = Brushes.Black,
                    BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                    BorderThickness = new Thickness(2),
                    FontFamily = new FontFamily(fontName),
                    FontWeight = ToWeight(isBold),
                    FontStyle = ToStyle(isItalic),
                    FontSize = Math.Max(canvasFontSize, 10),
                    // Hebrew/Arabic lines read right-to-left: base the box direction on the text
                    // so the caret, alignment and typing behave naturally while editing.
                    FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(lineText)
                        ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    MinWidth = Math.Max(cWidth + 20, 100),
                    // Fit the box height to the FONT (line height ~1.35em + borders), not the
                    // measured glyph bbox + a constant — the constant under-sizes big text and
                    // clips it. This tracks the selected text's height at any size.
                    Height = Math.Max(Math.Max(canvasFontSize, 10) * 1.35 + 6, 24),
                    Padding = new Thickness(2, 0, 2, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    AcceptsReturn = false,
                    Tag = new TextEditContext
                    {
                        PageIndex = pageIdx,
                        OriginalText = lineText,
                        CanvasBounds = new Rect(cLeft, cTop, cWidth, cHeight),
                        Position = new Point(cLeft, cTop),
                        FontSize = Math.Max(canvasFontSize, 10),
                        FontName = fontName,
                        IsBold = isBold,
                        IsItalic = isItalic,
                        EmbeddedFontBytes = embeddedBytes,
                        EmbeddedFamilyKey = embeddedKey,
                        FontDisplay = fontDisplay,
                    }
                };
                Canvas.SetLeft(tb, cLeft);
                Canvas.SetTop(tb, cTop);
                _activeCanvas.Children.Add(tb);
                _activeTextBox = tb;

                if (Scalpel.Services.BidiReorder.ContainsRtl(lineText))
                {
                    tb.FlowDirection = FlowDirection.RightToLeft;
                    int rtlProbe = Scalpel.Services.ArabicShaper.ContainsArabic(lineText) ? 0x0628 : 0x05D0;
                    if (!FontCovers(fontName, isBold, isItalic, rtlProbe))
                        tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic");
                }

                // Show white-out behind the edit box so original text is hidden
                var whiteout = new Rectangle
                {
                    Fill = Brushes.White,
                    Width = cWidth + 4,
                    Height = cHeight + 4,
                    IsHitTestVisible = false,
                    Tag = "EditWhiteout"
                };
                Canvas.SetLeft(whiteout, cLeft - 2);
                Canvas.SetTop(whiteout, cTop - 2);
                int tbIdx = _activeCanvas.Children.IndexOf(tb);
                _activeCanvas.Children.Insert(tbIdx, whiteout);

                tb.KeyDown += EditTextBox_KeyDown;
                tb.Loaded += (s, ev) =>
                {
                    tb.Focus();
                    Keyboard.Focus(tb);
                    tb.SelectAll();
                    tb.LostFocus += EditTextBox_LostFocus;
                };

                SetStatus("Editing text - Enter to save, Escape to cancel");
            }
            catch (Exception ex)
            {
                SetStatus($"Text edit error: {ex.Message}");
            }
        }

        /// <summary>Context data attached to an inline text edit TextBox via Tag.</summary>
        private class TextEditContext
        {
            public int PageIndex { get; set; }
            public string OriginalText { get; set; } = "";
            public Rect CanvasBounds { get; set; }
            public Point Position { get; set; }
            public double FontSize { get; set; }
            public string FontName { get; set; } = "Segoe UI";
            public bool IsBold { get; set; }
            public bool IsItalic { get; set; }
            /// <summary>The document's own embedded font bytes (extracted when the original font isn't
            /// installed), and the resolver key it was registered under. Used to redraw the edit in the
            /// exact font when it covers the typed text; null when unavailable/unusable.</summary>
            public byte[]? EmbeddedFontBytes { get; set; }
            public string? EmbeddedFamilyKey { get; set; }
            public string FontDisplay { get; set; } = "";
            /// <summary>Non-null when re-editing an already-committed annotation; update in place instead of adding a new one.</summary>
            public TextEditAnnotation? ExistingAnnotation { get; set; }
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitTextEdit();
                e.Handled = true;
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_activeTextBox is not null && _activeTextBox.Tag is TextEditContext)
            {
                Dispatcher.BeginInvoke(new Action(CommitTextEdit),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void CancelTextEdit()
        {
            if (_activeTextBox is null) return;
            var tb = _activeTextBox;
            _activeTextBox = null;
            _activeCanvas.Children.Remove(tb);
            // Remove the whiteout rectangle
            var whiteout = _activeCanvas.Children.OfType<Rectangle>()
                .FirstOrDefault(r => r.Tag is string s && s == "EditWhiteout");
            if (whiteout is not null)
                _activeCanvas.Children.Remove(whiteout);
            SetStatus("Text edit cancelled");
        }

        private void CommitTextEdit()
        {
            if (_activeTextBox is null || _activeTextBox.Tag is not TextEditContext ctx) return;
            var tb = _activeTextBox;
            _activeTextBox = null;
            string newText = tb.Text.Trim();
            _activeCanvas.Children.Remove(tb);

            // Remove the whiteout rectangle
            var whiteout = _activeCanvas.Children.OfType<Rectangle>()
                .FirstOrDefault(r => r.Tag is string s && s == "EditWhiteout");
            if (whiteout is not null)
                _activeCanvas.Children.Remove(whiteout);

            if (string.IsNullOrEmpty(newText) || newText == ctx.OriginalText)
            {
                SetStatus(newText == ctx.OriginalText ? "No changes made" : "Text edit cancelled (empty)");
                return;
            }

            // If we have the document's own embedded font, use it for the EDIT only when it covers
            // every character the user actually typed (a subset font can't render brand-new glyphs).
            // Otherwise fall back to the substitute font and warn that the original isn't installed.
            byte[]? embBytes = ctx.EmbeddedFontBytes;
            if (embBytes is null && ctx.EmbeddedFamilyKey is not null)
                Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(ctx.EmbeddedFamilyKey, ctx.IsBold, ctx.IsItalic, out embBytes);
            string? exactFamily = (ctx.EmbeddedFamilyKey is not null && embBytes is { Length: > 0 }
                                   && Scalpel.Services.TrueTypeCmap.CoversAllText(embBytes, newText))
                ? ctx.EmbeddedFamilyKey : null;
            // Had an exact font for the original text, but the new text adds glyphs it lacks → substitute + warn.
            if (exactFamily is null && ctx.EmbeddedFamilyKey is not null && !string.IsNullOrEmpty(ctx.FontDisplay))
                ShowToast(string.Format(Loc("Str_FontMissing_Body"), ctx.FontDisplay), ctx.FontDisplay);

            if (ctx.ExistingAnnotation is not null)
            {
                // Update the existing annotation in place — avoids duplicate whiteout layers
                ctx.ExistingAnnotation.NewContent = newText;
                ctx.ExistingAnnotation.ExactFontFamily = exactFamily;
            }
            else
            {
                var edit = new TextEditAnnotation
                {
                    PageIndex = ctx.PageIndex,
                    OriginalBounds = ctx.CanvasBounds,
                    Position = ctx.Position,
                    NewContent = newText,
                    OriginalContent = ctx.OriginalText,
                    FontSize = ctx.FontSize,
                    FontName = ctx.FontName,
                    IsBold = ctx.IsBold,
                    IsItalic = ctx.IsItalic,
                    ExactFontFamily = exactFamily,
                };
                AddAnnotation(edit);
            }
            RenderAllAnnotations(ctx.PageIndex);
            SetStatus($"Text edited: \"{ctx.OriginalText}\" -> \"{newText}\"");
        }

    }
}
